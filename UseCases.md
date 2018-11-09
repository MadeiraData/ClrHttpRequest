# Use Cases

See below several different use cases for the clr_http_request function:


## Zendesk API

The example script below uses clr_http_request to look for a ticket of a given requester with a given subject.
If such a ticket doesn't exist, create a new one. Otherwise, post a comment to the existing ticket.

```
-- Script Parameters:
DECLARE @RequesterEmail NVARCHAR(4000) = N'some-end-user@some_customer_company.com'
DECLARE @Subject NVARCHAR(MAX) = N'This is an automated alert'
DECLARE @Body NVARCHAR(MAX) = N'This is the body of the message. It can be HTML formatted.'
DECLARE @Priority NVARCHAR(4000) = N'normal' -- possible values: low, normal, high, critical

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


if (ISNULL(@ticket,'') = '')
BEGIN
	-- ticket doesn't already exist. create new and get its info in return:
	DECLARE @ticketbody NVARCHAR(MAX) = '{"ticket": {"subject": "' + @Subject + '", "comment": { "body": "' + @Body + '", "html_body": "' + @Body + '" }, "type" : "incident", "priority" : "' + @Priority + '", "requester": { "locale_id": 8, "email": "' + @RequesterEmail + '" }," }] }}'
	
  -- More magic here:
	SET @ticket = [dbo].[clr_http_request]
        (
            'POST',
            @zendesk_address + '/api/v2/tickets.json',
            @ticketbody,
            @headers,
            300000,
            0,
            0
        ).value('/Response[1]/Body[1]', 'NVARCHAR(MAX)')

END
else
BEGIN
	-- ticket already exists. add comment:
	SET @uri = JSON_VALUE(@ticket, '$.url')

	DECLARE @commentbody NVARCHAR(MAX) = '{"ticket": {"comment": { "body": "The alert has just been fired again on ' + CONVERT(nvarchar(25), GETDATE(), 121) + '. This is an automated message.", "author_id": "' + JSON_VALUE(@ticket, '$.submitter_id') + '" }}}'
	DECLARE @comment NVARCHAR(MAX)
	
  -- More magic here:
	SET @comment = [dbo].[clr_http_request]
        (
            'PUT',
            @uri,
            @commentbody,
            @headers,
            300000,
            0,
            0
        ).value('/Response[1]/Body[1]', 'NVARCHAR(MAX)')
END
```

## Slack Workspace App API

The following example demonstrate how to use clr_http_request with a Slack Workspace bot:

```
DECLARE
	@RecipientUser NVARCHAR(MAX) = 'MySlackUsername',
	@PlainText NVARCHAR(MAX) = 'Hi <@user>. This is a reminder for you about that thing you need to do.',
	@RichText NVARCHAR(MAX) = 'Hi <@user>. This is a reminder for you about that thing you need to do.',
	@Title NVARCHAR(MAX) = 'This is the title',
	@BotUserOAuthToken NVARCHAR(MAX) = 'xoxb-your_bot_user_OAuth_token_here',
	@UserMentionPlaceHolder NVARCHAR(MAX) = '<@user>'

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ARITHABORT ON;

-- Init headers
DECLARE @headers NVARCHAR(4000) = '<Headers><Header Name="Content-Type">application/json</Header></Headers>'
DECLARE @uri NVARCHAR(4000)
DECLARE @response XML, @results NVARCHAR(MAX), @recipient NVARCHAR(MAX)

-- Step 1: Use the users.list API endpoint to get a list of all workspace users
SET @uri = 'https://slack.com/api/users.list?token=' + @BotUserOAuthToken
SET @response = [dbo].[clr_http_request]
        (
            'GET',
            @uri,
            NULL,
            @headers,
            300000,
            0,
            0
        )

SET @results = @response.value('/Response[1]/Body[1]', 'NVARCHAR(MAX)')

-- Step 2: Find the relevant user based on the @RecipientUser parameter
SELECT @recipient = [value]
FROM OPENJSON(@results, '$.members')
WHERE JSON_VALUE([value], '$.name') = @RecipientUser

-- Step 3: Prepare the message body
DECLARE @msgbody NVARCHAR(MAX)

if ((@RichText = '' AND @Title = '') AND @PlainText <> '')
	SET @msgbody = '{"text":"' + @PlainText + '"}'
else if (@RichText <> '' OR @Title <> '')
	SET @msgbody = '[ { "text": "' + @RichText + '","title": "' + @Title + '","fallback": "' + @PlainText + '" }]'

if (@msgbody <> '')
BEGIN
	-- Use REPLACE to replace the <@user> placeholder with the correct User ID
    SET @msgbody = Replace(@msgbody, @UserMentionPlaceHolder, '<@' + JSON_VALUE(@recipient, '$.id') + '>')

	-- Step 4: Use the chat.PostMessage API endpoint to send a direct message to the recipient via the Slackbot
    SET @uri = 'https://slack.com/api/chat.postMessage?token=' + @BotUserOAuthToken + '&channel=' + JSON_VALUE(@recipient, '$.id') + '&attachments=' + @msgbody
    SET @response = [dbo].[clr_http_request]
        (
            'POST',
            @uri,
            NULL,
            @headers,
            300000,
            0,
            0
        )
		
	SET @results = @response.value('/Response[1]/Body[1]', 'NVARCHAR(MAX)')
	
	-- Step 5: Analyze response
    if (JSON_VALUE(@results, '$.ok') = 'true')
        PRINT 'ok'
    else
	BEGIN
        SET @results = JSON_VALUE(@results, '$.message')
		RAISERROR(@results,16,1);
	END
END
else
	RAISERROR(N'no message input received',16,1);
```

## Stack Overflow API

Here is an example to retrieve Stack Overflow questions from their API.

```
-- Query the Stack Overflow API and get a response
DECLARE @response XML = 
    [dbo].[clr_http_request]
        (
            'GET', 'http://api.stackexchange.com/2.2/questions?site=stackoverflow', 
            NULL, NULL, 300000, 1, 0
        );

-- Extract just the body of the response (expecting JSON)
DECLARE @response_json NVARCHAR(MAX) = @response.value('Response[1]/Body[1]', 'NVARCHAR(MAX)');

-- Parse the JSON into a tabular format
SELECT 
    B.[question_id],
    B.[title],
    B.[tags],
    B.[is_answered],
    B.[view_count],
    B.[answer_count],
    B.[score]
FROM OPENJSON(@response_json) WITH ([items] NVARCHAR(MAX) AS JSON) A
CROSS APPLY OPENJSON(A.[items]) WITH 
    (
        [question_id] INT,
        [title] NVARCHAR(MAX),
        [tags] NVARCHAR(MAX) AS JSON,
        [is_answered] BIT,
        [view_count] INT,
        [answer_count] INT,
        [score] INT
    ) B;
```
## Google AdWords' API

This example will get an access token and use that to pull a performance report from Google AdWords.

```
-- Authentication variables
DECLARE @refresh_token VARCHAR(500) = '...';
DECLARE @client_id VARCHAR(500) = '...';
DECLARE @client_secret VARCHAR(500) = '...';
DECLARE @client_customer_id VARCHAR(500) = '...';
DECLARE @developer_token VARCHAR(500) = '...';

-- Use the AdWords Query Language to define a query for the Keywords Performance Report and specify desired format
DECLARE @awql VARCHAR(MAX) = '
    SELECT CampaignId, CampaignName, AdGroupId, AdGroupName, Id, Criteria, Device, Date, Impressions, Clicks, Cost, AveragePosition 
    FROM KEYWORDS_PERFORMANCE_REPORT WHERE Impressions > 0 DURING 20180801,20180805
';
DECLARE @fmt VARCHAR(50) = 'XML';

-- Get access token
DECLARE @access_token VARCHAR(500) = JSON_VALUE(
    [dbo].[clr_http_request]
        (
            'POST',
            'https://www.googleapis.com/oauth2/v4/token',
            CONCAT('grant_type=refresh_token&refresh_token=', @refresh_token, '&client_id=', @client_id, '&client_secret=', @client_secret),
            NULL,
            300000,
            0,
            0
        ).value('/Response[1]/Body[1]', 'NVARCHAR(MAX)'),
    '$.access_token'
);

-- Get report
DECLARE @report_xml XML =
    CAST(REPLACE(
        [dbo].[clr_http_request]
            (
                'POST',
                'https://adwords.google.com/api/adwords/reportdownload/v201802',
                CONCAT('__fmt=', @fmt, '&__rdquery=', @awql),
                CONCAT('
                    <Headers>
                        <Header Name="Authorization">Bearer ', @access_token, '</Header>
                        <Header Name="developerToken">', @developer_token, '</Header>
                        <Header Name="clientCustomerId">', @client_customer_id, '</Header>
                    </Headers>
                '),
                300000,
                1,
                0
            ).value('/Response[1]/Body[1]', 'NVARCHAR(MAX)'),
        '<?xml version=''1.0'' encoding=''UTF-8'' standalone=''yes''?>',
        ''
    ) AS XML);

-- Parse report XML
SELECT 
    A.[row].value('@campaignID', 'BIGINT') [campaign_id],
    A.[row].value('@campaign', 'VARCHAR(500)') [campaign_name],
    A.[row].value('@adGroupID', 'BIGINT') [ad_group_id],
    A.[row].value('@adGroup', 'VARCHAR(500)') [ad_group_name],
    A.[row].value('@keywordID', 'BIGINT') [keyword_id],
    A.[row].value('@keyword', 'VARCHAR(500)') [keyword],
    A.[row].value('@device', 'VARCHAR(50)') [device],
    A.[row].value('@day', 'DATE') [date],
    A.[row].value('@impressions', 'INT') [impressions],
    A.[row].value('@clicks', 'INT') [clicks],
    A.[row].value('@cost', 'BIGINT') [cost],
    A.[row].value('@avgPosition', 'FLOAT') [average_position]
FROM @report_xml.nodes('/report/table/row') A ([row]);
```

For more info:
http://www.sqlservercentral.com/articles/SQLCLR/177834/
