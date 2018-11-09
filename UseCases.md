# Use Cases

See below several different use cases for the clr_http_request function:


## Zendesk

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

## Slack

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
