using Microsoft.SqlServer.Server;
using System;
using System.ComponentModel;
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
/// Update 2020-08-17: Added UTF8 support, and case-insensitive headers.
/// </summary>
public partial class UserDefinedFunctions
{
    [Microsoft.SqlServer.Server.SqlFunction]
    public static SqlXml clr_http_request(
        [SqlFacet(MaxSize = 10)] SqlString requestMethod,
        [SqlFacet(MaxSize = -1, IsNullable = false)] SqlString url,
        [SqlFacet(MaxSize = -1, IsNullable = true)] SqlString parameters,
        [SqlFacet(MaxSize = -1, IsNullable = true)] SqlString headers,
        SqlInt32 timeout,
        SqlBoolean autoDecompress,
        SqlBoolean convertResponseToBas64
        //, bool debug
        )
    {
        // Default values
        if (requestMethod.IsNull)
            requestMethod = "GET";

        if (timeout.IsNull)
            timeout = 30000;

        if (autoDecompress.IsNull)
            autoDecompress = false;

        if (convertResponseToBas64.IsNull)
            convertResponseToBas64 = false;

        // If GET request, and there are parameters, build into url
        if (requestMethod.Value.ToUpper() == "GET" && !parameters.IsNull && !String.IsNullOrEmpty(parameters.Value))
        {
            url += (url.Value.IndexOf('?') > 0 ? "&" : "?") + parameters;
        }

        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls;

        // Create an HttpWebRequest with the url
        var request = (WebRequest)WebRequest.Create(url.Value);

        // Add in any headers provided
        bool contentLengthSetFromHeaders = false;
        bool contentTypeSetFromHeaders = false;
        if (!headers.IsNull && !String.IsNullOrEmpty(headers.Value))
        {
            // Parse provided headers as XML and loop through header elements
            var xmlData = XElement.Parse(headers.Value);
            foreach (XElement headerElement in xmlData.Descendants())
            {
                // Retrieve header's name and value
                var headerName = headerElement.Attribute("Name").Value;
                var headerValue = headerElement.Value;

                // Some headers cannot be set by request.Headers.Add() and need to set the HttpWebRequest property directly
                switch (headerName.ToUpper())
                {
                    //case "ACCEPT":
                    //    request.Accept = headerValue;
                    //    break;
                    //case "CONNECTION":
                    //    request.Connection = headerValue;
                    //    break;
                    case "CONTENT-LENGTH":
                        request.ContentLength = long.Parse(headerValue);
                        contentLengthSetFromHeaders = true;
                        break;
                    case "CONTENT-TYPE":
                        request.ContentType = headerValue;
                        contentTypeSetFromHeaders = true;
                        break;
                    //case "DATE":
                    //    request.Date = DateTime.Parse(headerValue);
                    //    break;
                    //case "EXPECT":
                    //    request.Expect = headerValue;
                    //    break;
                    //case "HOST":
                    //    request.Host = headerValue;
                    //    break;
                    //case "IF-MODIFIED-SINCE":
                    //    request.IfModifiedSince = DateTime.Parse(headerValue);
                    //    break;
                    //case "RANGE":
                    //    var parts = headerValue.Split('-');
                    //    if (parts.Length < 2)
                    //    {
                    //        throw new FormatException("Range must be specified in a format of start-end");
                    //    }
                    //    request.AddRange(int.Parse(parts[0]), int.Parse(parts[1]));
                    //    break;
                    //case "REFERER":
                    //    request.Referer = headerValue;
                    //    break;
                    //case "TRANSFER-ENCODING":
                    //    request.TransferEncoding = headerValue;
                    //    break;
                    //case "USER-AGENT":
                    //    request.UserAgent = headerValue;
                    //    break;
                    case "AUTHORIZATION-BASIC-CREDENTIALS":
                        request.Headers.Add("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(headerValue)));
                        break;
                    case "AUTHORIZATION-NETWORK-CREDENTIALS":
                        var values = headerValue.Split(':');
                        if (values.Length < 2)
                        {
                            throw new FormatException("When specifying Authorization-Network-Credentials headers, please set the value in a format of username:password");
                        }
                        request.Credentials = new NetworkCredential(headerValue.Split(':')[0], headerValue.Split(':')[1]);
                        break;
                    default: // other headers
                        request.Headers.Add(headerName, headerValue);
                        break;
                }
            }
        }

        // Set the method, timeout, and decompression
        request.Method = (requestMethod.IsNull) ? "GET" : requestMethod.Value.ToUpper();
        request.Timeout = (timeout.IsNull) ? 30000 : timeout.Value;
        if (!autoDecompress.IsNull && autoDecompress.Value)
        {
            request.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip;
        }

        // Add in non-GET parameters provided
        if (requestMethod.Value.ToUpper() != "GET" && !parameters.IsNull && !String.IsNullOrEmpty(parameters.Value))
        {
            // Convert to byte array
            var parameterData = Encoding.UTF8.GetBytes(parameters.Value);

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
                stream.Close();
            }
        }

        // Retrieve results from response
        XElement returnXml;
        using (var response = (WebResponse)request.GetResponse())
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
                        stream.CopyTo(memoryStream as Stream);

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
                    //new XElement("CharacterSet", response.CharacterSet),
                    //new XElement("ContentEncoding", response.ContentEncoding),
                    new XElement("ContentLength", response.ContentLength),
                    new XElement("ContentType", response.ContentType),
                    //new XElement("CookiesCount", response.Cookies.Count),
                    new XElement("HeadersCount", response.Headers.Count),
                    headersXml,
                    new XElement("IsFromCache", response.IsFromCache),
                    new XElement("IsMutuallyAuthenticated", response.IsMutuallyAuthenticated),
                    //new XElement("LastModified", response.LastModified),
                    //new XElement("Method", response.Method),
                    //new XElement("ProtocolVersion", response.ProtocolVersion),
                    new XElement("ResponseUri", response.ResponseUri),
                    //new XElement("Server", response.Server),
                    //new XElement("StatusCode", response.StatusCode),
                    //new XElement("StatusNumber", ((int)response.StatusCode)),
                    //new XElement("StatusDescription", response.StatusDescription),
                    //new XElement("SupportsHeaders", response.SupportsHeaders),
                    new XElement("Body", responseString)
                );
            
            response.Close();
        }

        // Return data
        return new SqlXml(returnXml.CreateReader());
    }
}
