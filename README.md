# ClrHttpRequest

SQL Server CLR function for running REST methods over HTTP.

To download the latest version, please [visit the Releases page](https://github.com/MadeiraData/ClrHttpRequest/releases).

This project is a fork of the project initially published By Eilert Hjelmeseth, 2018/10/11 here:
http://www.sqlservercentral.com/articles/SQLCLR/177834/

Eilhert's GitHub project: https://github.com/eilerth/sqlclr-http-request

My version extends the project by adding the following:

* Usage of TLS1.2 security protocol (nowadays a global standard).
* Two new authentication methods:
  * Authorization-Basic-Credentials (Basic authorization using Base64 credentials)
  * Authorization-Network-Credentials (creates a new `NetworkCredential` object and assigns it to the `Credentials` property of the request)
* Addition of a proper PreDeployment script which takes care of CLR assembly signing without requiring the TRUSTWORTHY database setting.
* Added UTF8 encoding support instead of ASCII.
* Added support for case-insensitive headers.
  
The following code was added in clr_http_request.cs, line 19:
```
ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
```

The following code was added in line 79:
```
case "Authorization-Basic-Credentials":
    request.Headers.Add("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(headerValue)));
    break;
case "Authorization-Network-Credentials":
    request.Credentials = new NetworkCredential(headerValue.Split(':')[0], headerValue.Split(':')[1]);
    break;
```

These changes allow the SQL Server function to work with advanced services such as Zendesk.
For example:

```
-- Credentials info: Username (email address) must be followed by /token when using API key
DECLARE @credentials NVARCHAR(4000) = 'agent@company_domain.com/token:api_token_key_here'
DECLARE @headers NVARCHAR(4000) = '<Headers><Header Name="Content-Type">application/json</Header><Header Name="Authorization-Basic-Credentials">' + @credentials + '</Header></Headers>'

-- Global Zendesk Settings:
DECLARE @zendesk_address NVARCHAR(400) = 'https://your_subdomain_here.zendesk.com'

-- Look for existing tickets based on @RequesterEmail:
SET @uri = @zendesk_address + '/api/v2/search.json?query=type:ticket status<solved requester:' + @RequesterEmail

DECLARE @tickets NVARCHAR(MAX), @ticket NVARCHAR(MAX)

-- This is where the magic happens:
SET @tickets = [dbo].[clr_http_request]
        (
            'GET',
            @uri,
            NULL,
            @headers,
            300000,
            0,
            0
        ).value('/Response[1]/Body[1]', 'NVARCHAR(MAX)')

-- check if ticket exists based on @Subject:
SELECT @ticket = [value]
FROM OPENJSON(@tickets, '$.results')
WHERE JSON_VALUE([value], '$.subject') = @Subject
AND JSON_VALUE([value], '$.status') IN ('new', 'open', 'pending')

SELECT uri = JSON_VALUE(@ticket, '$.url'), submitter = JSON_VALUE(@ticket, '$.submitter_id')
```
For more use cases visit here: https://github.com/EitanBlumin/ClrHttpRequest/blob/master/UseCases.md

For more info on using the Zendesk API, visit here: https://developer.zendesk.com/rest_api/docs/core/introduction
