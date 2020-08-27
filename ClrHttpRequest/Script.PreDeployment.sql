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
	DECLARE @InitAdvanced INT;
	SELECT @InitAdvanced = CONVERT(int, value) FROM sys.configurations WHERE name = 'show advanced options';

	IF @InitAdvanced = 0
	BEGIN
		EXEC sp_configure 'show advanced options', 1;
		RECONFIGURE;
	END

    EXEC sp_configure 'clr enabled', 1;
    RECONFIGURE;

	IF @InitAdvanced = 0
	BEGIN
		EXEC sp_configure 'show advanced options', 0;
		RECONFIGURE;
	END
END
GO
IF NOT EXISTS (select * from sys.asymmetric_keys WHERE name = '$(CLRKeyName)')
BEGIN
	BEGIN TRY
		PRINT N'Creating encryption key from: $(PathToSignedDLL)'
		create asymmetric key [$(CLRKeyName)]
		from executable file = '$(PathToSignedDLL)'
	END TRY
	BEGIN CATCH
		IF ERROR_NUMBER() = 15396
		BEGIN
			RAISERROR(N'An encryption key with the same thumbprint was already created in this database with a different name.',0,1);
			IF EXISTS(
				SELECT *
				FROM sys.asymmetric_keys AS ak
				LEFT JOIN sys.syslogins AS l ON l.sid = ak.sid
				WHERE l.sid IS NULL
			)
			BEGIN
				RAISERROR(N'Please make sure that there is also a login for the existing encryption key.',0,1);
			END
		END
	END CATCH
END
GO
IF NOT EXISTS (select name from sys.syslogins where name = '$(CLRLoginName)')
AND EXISTS (select * from sys.asymmetric_keys WHERE name = '$(CLRKeyName)')
BEGIN
	PRINT N'Creating login from encryption key...'
	create login [$(CLRLoginName)] from asymmetric key [$(CLRKeyName)];
END
GO
grant unsafe assembly to [$(CLRLoginName)];
GO
-- Return execution context to intended target database
USE [$(DatabaseName)];
GO