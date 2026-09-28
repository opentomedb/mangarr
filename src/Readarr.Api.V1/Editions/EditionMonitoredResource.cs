namespace NzbDrone.Api.V1.Editions
{
    // Body of PUT /api/v1/edition/{id}: the per-edition monitor toggle (a light-novel volume's EPUB
    // and Audio editions are monitored on their own).
    public class EditionMonitoredResource
    {
        public bool Monitored { get; set; }
    }
}
