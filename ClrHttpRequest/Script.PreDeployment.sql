/*
 Pre-Deployment Script Template							
--------------------------------------------------------------------------------------
 This file contains SQL statements that will be executed before the build script.	
 Use SQLCMD syntax to include a file in the pre-deployment script.			
 Example:      :r .\myfile.sql								
 Use SQLCMD syntax to reference a variable in the pre-deployment script.		
 Example:      :setvar TableName MyTable							
               SELECT * FROM [$(TableName)]					
--------------------------------------------------------------------------------------
*/

use [master];
GO
-- Make sure CLR is enabled in the instance
IF (SELECT value_in_use FROM sys.configurations WHERE name = 'clr enabled') = 0
BEGIN
    PRINT N'Enabling CLR...'
    EXEC sp_configure 'clr enabled', 1;
    RECONFIGURE;
END
GO
IF NOT EXISTS (select * from sys.asymmetric_keys WHERE name = 'clr_http_request_pkey')
	create asymmetric key clr_http_request_pkey
	from executable file = '$(PathToSignedDLL)'
GO
IF NOT EXISTS (select name from sys.syslogins where name = 'clr_http_request_login')
	create login clr_http_request_login from asymmetric key clr_http_request_pkey;
GO
grant unsafe assembly to clr_http_request_login;
GO
-- Return execution context to intended target database
USE [$(DatabaseName)];
GO