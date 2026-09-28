using System.Collections.Generic;
using NzbDrone.Common.Localization;

namespace NzbDrone.Core.Download.Clients.DownloadStation.Responses
{
    public class DiskStationError
    {
        private static readonly Dictionary<int, ServerText> CommonMessages;
        private static readonly Dictionary<int, ServerText> AuthMessages;
        private static readonly Dictionary<int, ServerText> DownloadStationTaskMessages;
        private static readonly Dictionary<int, ServerText> FileStationMessages;

        static DiskStationError()
        {
            CommonMessages = new Dictionary<int, ServerText>
            {
                { 100, new ServerText("Unknown error") },
                { 101, new ServerText("Invalid parameter") },
                { 102, new ServerText("The requested API does not exist") },
                { 103, new ServerText("The requested method does not exist") },
                { 104, new ServerText("The requested version does not support the functionality") },
                { 105, new ServerText("The logged in session does not have permission") },
                { 106, new ServerText("Session timeout") },
                { 107, new ServerText("Session interrupted by duplicate login") },
                { 119, new ServerText("SID not found") }
            };

            AuthMessages = new Dictionary<int, ServerText>
            {
                { 400, new ServerText("No such account or incorrect password") },
                { 401, new ServerText("Disabled account") },
                { 402, new ServerText("Denied permission") },
                { 403, new ServerText("2-step authentication code required") },
                { 404, new ServerText("Failed to authenticate 2-step authentication code") },
                { 406, new ServerText("Enforce to authenticate with 2-factor authentication code") },
                { 407, new ServerText("Blocked IP source") },
                { 408, new ServerText("Expired password cannot change") },
                { 409, new ServerText("Expired password") },
                { 410, new ServerText("Password must be changed") }
            };

            DownloadStationTaskMessages = new Dictionary<int, ServerText>
            {
                { 400, new ServerText("File upload failed") },
                { 401, new ServerText("Max number of tasks reached") },
                { 402, new ServerText("Destination denied") },
                { 403, new ServerText("Destination does not exist") },
                { 404, new ServerText("Invalid task id") },
                { 405, new ServerText("Invalid task action") },
                { 406, new ServerText("No default destination") },
                { 407, new ServerText("Set destination failed") },
                { 408, new ServerText("File does not exist") }
            };

            FileStationMessages = new Dictionary<int, ServerText>
            {
                { 160, new ServerText("Permission denied. Give your user access to FileStation.") },
                { 400, new ServerText("Invalid parameter of file operation") },
                { 401, new ServerText("Unknown error of file operation") },
                { 402, new ServerText("System is too busy") },
                { 403, new ServerText("Invalid user does this file operation") },
                { 404, new ServerText("Invalid group does this file operation") },
                { 405, new ServerText("Invalid user and group does this file operation") },
                { 406, new ServerText("Can’t get user/group information from the account server") },
                { 407, new ServerText("Operation not permitted") },
                { 408, new ServerText("No such file or directory") },
                { 409, new ServerText("Non-supported file system") },
                { 410, new ServerText("Failed to connect internet-based file system (ex: CIFS)") },
                { 411, new ServerText("Read-only file system") },
                { 412, new ServerText("Filename too long in the non-encrypted file system") },
                { 413, new ServerText("Filename too long in the encrypted file system") },
                { 414, new ServerText("File already exists") },
                { 415, new ServerText("Disk quota exceeded") },
                { 416, new ServerText("No space left on device") },
                { 417, new ServerText("Input/output error") },
                { 418, new ServerText("Illegal name or path") },
                { 419, new ServerText("Illegal file name") },
                { 420, new ServerText("Illegal file name on FAT file system") },
                { 421, new ServerText("Device or resource busy") },
                { 599, new ServerText("No such task of the file operation") },
            };
        }

        public int Code { get; set; }

        public bool SessionError => Code == 105 || Code == 106 || Code == 107 || Code == 119;

        // Server messages (2026-09-27, follow-ups): a ServerText, so DiskStationProxyBase's "Failed to {0}.
        // Reason: {1}" shows its reason in the UI language too (ServerTestDiskStationError* keys); its English is
        // the string this returned before.
        public ServerText GetMessage(DiskStationApi api)
        {
            if (api == DiskStationApi.Auth && AuthMessages.ContainsKey(Code))
            {
                return AuthMessages[Code];
            }

            if ((api == DiskStationApi.DownloadStationTask || api == DiskStationApi.DownloadStation2Task) && DownloadStationTaskMessages.ContainsKey(Code))
            {
                return DownloadStationTaskMessages[Code];
            }

            if (api == DiskStationApi.FileStationList && FileStationMessages.ContainsKey(Code))
            {
                return FileStationMessages[Code];
            }

            if (CommonMessages.ContainsKey(Code))
            {
                return CommonMessages[Code];
            }

            return new ServerText("{0} - Unknown error", Code);
        }
    }
}
