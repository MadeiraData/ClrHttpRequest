using System;
using System.Data.SqlTypes;
using System.IO;
using System.Net;
using System.Text;
using System.Xml.Linq;

/// <summary>
/// clr_http_request was originally written by Eilert Hjelmeseth
/// and was published on 2018/10/11 here: http://www.sqlservercentral.com/articles/SQLCLR/177834/
/// This version has minor improvements that allow it to support TLS1.2 security protocol
/// and a couple of additional authorization methods.
/// </summary>
public partial class UserDefinedFunctions
{
    [Microsoft.SqlServer.Server.SqlFunction]
    public static SqlXml clr_http_request(string requestMethod, string url, string parameters, string headers, int timeout, bool autoDecompress, bool convertResponseToBas64) //, bool debug
    {
        // If GET request, and there are parameters, build into url
        if (requestMethod.ToUpper() == "GET" && !string.IsNullOrWhiteSpace(parameters))
        {
            url += (url.IndexOf('?') > 0 ? "&" : "?") + parameters;
        }

        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

        // Create an HttpWebRequest with the url
        var request = (HttpWebRequest)HttpWebRequest.Create(url);

        // Add in any headers provided
        bool contentLengthSetFromHeaders = false;
        bool contentTypeSetFromHeaders = false;
        if (!string.IsNullOrWhiteSpace(headers))
        {
            // Parse provided headers as XML and loop through header elements
            var xmlData = XElement.Parse(headers);
            foreach (XElement headerElement in xmlData.Descendants())
            {
                // Retrieve header's name and value
                var headerName = headerElement.Attribute("Name").Value;
                var headerValue = headerElement.Value;

                // Some headers cannot be set by request.Headers.Add() and need to set the HttpWebRequest property directly
                switch (headerName) 
                {
                    case "Accept":
                        request.Accept = headerValue;
                        break;
                    case "Connection":
                        request.Connection = headerValue;
                        break;
                    case "Content-Length":
                        request.ContentLength = long.Parse(headerValue);
                        contentLengthSetFromHeaders = true;
                        break;
                    case "Content-Type":
                        request.ContentType = headerValue;
                        contentTypeSetFromHeaders = true;
                        break;
                    case "Date":
                        request.Date = DateTime.Parse(headerValue);
                        break;
                    case "Expect":
                        request.Expect = headerValue;
                        break;
                    case "Host":
                        request.Host = headerValue;
                        break;
                    case "If-Modified-Since":
                        request.IfModifiedSince = DateTime.Parse(headerValue);
                        break;
                    case "Range":
                        var parts = headerValue.Split('-');
                        request.AddRange(int.Parse(parts[0]), int.Parse(parts[1]));
                        break;
                    case "Referer":
                        request.Referer = headerValue;
                        break;
                    case "Transfer-Encoding":
                        request.TransferEncoding = headerValue;
                        break;
                    case "User-Agent":
                        request.UserAgent = headerValue;
                        break;
                    case "Authorization-Basic-Credentials":
                        request.Headers.Add("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(headerValue)));
                        break;
                    case "Authorization-Network-Credentials":
                        request.Credentials = new NetworkCredential(headerValue.Split(':')[0], headerValue.Split(':')[1]);
                        break;
                    default: // other headers
                        request.Headers.Add(headerName, headerValue);
                        break;
                }
            }
        }

        // Set the method, timeout, and decompression
        request.Method = requestMethod.ToUpper();
        request.Timeout = timeout;
        if (autoDecompress)
        {
            request.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip;
        }

        // Add in non-GET parameters provided
        if (requestMethod.ToUpper() != "GET" && !string.IsNullOrWhiteSpace(parameters))
        {
            // Convert to byte array
            var parameterData = Encoding.ASCII.GetBytes(parameters);

            // Set content info
            if (!contentLengthSetFromHeaders)
            {
                request.ContentLength = parameterData.Length;
            }
            if (!contentTypeSetFromHeaders)
            {
                request.ContentType = "application/x-www-form-urlencoded";
            }

            // Add data to request stream
            using (var stream = request.GetRequestStream())
            {
                stream.Write(parameterData, 0, parameterData.Length);
            }
        }

        // Retrieve results from response
        XElement returnXml;
        using (var response = (HttpWebResponse)request.GetResponse())
        {
            // Get headers (loop through response's headers)
            var headersXml = new XElement("Headers");
            var responseHeaders = response.Headers;
            for (int i = 0; i < responseHeaders.Count; ++i)
            {
                // Get values for this header
                var valuesXml = new XElement("Values");
                foreach (string value in responseHeaders.GetValues(i))
                {
                    valuesXml.Add(new XElement("Value", value));
                }

                // Add this header with its values to the headers xml
                headersXml.Add(
                    new XElement("Header",
                        new XElement("Name", responseHeaders.GetKey(i)),
                        valuesXml
                    )
                );
            }

            // Get the response body
            var responseString = String.Empty;
            using (var stream = response.GetResponseStream())
            {
                // If requested to convert to base 64 string, use memory stream, otherwise stream reader
                if (convertResponseToBas64)
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        // Copy response stream to memory stream
                        stream.CopyTo(memoryStream);

                        // Convert memory stream to a byte array
                        var bytes = memoryStream.ToArray();

                        // Convert to base 64 string
                        responseString = Convert.ToBase64String(bytes);
                    }
                }
                else
                {
                    using (var reader = new StreamReader(stream))
                    {
                        // Retrieve response string
                        responseString = reader.ReadToEnd();
                    }
                }
            }

            // Assemble reponse XML from details of HttpWebResponse
            returnXml =
                new XElement("Response",
                    new XElement("CharacterSet", response.CharacterSet),
                    new XElement("ContentEncoding", response.ContentEncoding),
                    new XElement("ContentLength", response.ContentLength),
                    new XElement("ContentType", response.ContentType),
                    new XElement("CookiesCount", response.Cookies.Count),
                    new XElement("HeadersCount", response.Headers.Count),
                    headersXml,
                    new XElement("IsFromCache", response.IsFromCache),
                    new XElement("IsMutuallyAuthenticated", response.IsMutuallyAuthenticated),
                    new XElement("LastModified", response.LastModified),
                    new XElement("Method", response.Method),
                    new XElement("ProtocolVersion", response.ProtocolVersion),
                    new XElement("ResponseUri", response.ResponseUri),
                    new XElement("Server", response.Server),
                    new XElement("StatusCode", response.StatusCode),
                    new XElement("StatusNumber", ((int)response.StatusCode)),
                    new XElement("StatusDescription", response.StatusDescription),
                    new XElement("SupportsHeaders", response.SupportsHeaders),
                    new XElement("Body", responseString)
                );
        }

        // Return data
        return new SqlXml(returnXml.CreateReader());
    }
}
