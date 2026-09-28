using System;

namespace NzbDrone.Common.Exceptions
{
    public class ReadarrStartupException : NzbDroneException
    {
        public ReadarrStartupException(string message, params object[] args)
            : base("Mangarr failed to start: " + string.Format(message, args))
        {
        }

        public ReadarrStartupException(string message)
            : base("Mangarr failed to start: " + message)
        {
        }

        public ReadarrStartupException()
            : base("Mangarr failed to start")
        {
        }

        public ReadarrStartupException(Exception innerException, string message, params object[] args)
            : base("Mangarr failed to start: " + string.Format(message, args), innerException)
        {
        }

        public ReadarrStartupException(Exception innerException, string message)
            : base("Mangarr failed to start: " + message, innerException)
        {
        }

        public ReadarrStartupException(Exception innerException)
            : base("Mangarr failed to start: " + innerException.Message)
        {
        }
    }
}
