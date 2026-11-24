using System.Net;

internal static partial class CatalogOnlineFixture
{
    private sealed class FixtureHttp : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string body = request.RequestUri!.AbsolutePath switch
            {
                "/api/satellites/" when request.RequestUri.Query.Contains("page=2", StringComparison.Ordinal) =>
                    """[{"sat_id":"satnogs-100002","norad_cat_id":100002,"name":"Online Test","status":"in orbit"}]""",
                "/api/satellites/" =>
                    """{"results":[{"sat_id":"fixture-100001","norad_cat_id":100001,"name":"OMM Test","status":"in orbit"}],"next":"?page=2"}""",
                "/api/transmitters/" =>
                    """[{"uuid":"omm-tx","sat_id":"fixture-100001","downlink_low":145999000,"mode":"USB","alive":true},{"uuid":"online-tx","sat_id":"satnogs-100002","uplink_low":435600000,"mode":"FM","alive":true}]""",
                "/api/tle/" =>
                    """[{"sat_id":"unused","norad_cat_id":5,"tle1":"1 00005U 58002B   00179.78495062  .00000023  00000-0  28098-4 0  4753","tle2":"2 00005  34.2682 348.7242 1859667 331.7664  19.3264 10.82419157413667"}]""",
                "/gp.csv" => Csv,
                _ => throw new InvalidOperationException("Unexpected catalog request: " + request.RequestUri)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private const string Csv =
        "OBJECT_NAME,OBJECT_ID,EPOCH,MEAN_MOTION,ECCENTRICITY,INCLINATION,RA_OF_ASC_NODE,ARG_OF_PERICENTER,MEAN_ANOMALY,EPHEMERIS_TYPE,CLASSIFICATION_TYPE,NORAD_CAT_ID,ELEMENT_SET_NO,REV_AT_EPOCH,BSTAR,MEAN_MOTION_DOT,MEAN_MOTION_DDOT\n" +
        "OLD-OMM,2026-001A,2026-10-07T00:00:00.000000,15.50000000,0.0010000,51.6000,120.0000,80.0000,25.0000,0,U,100001,998,12330,0.0001,0.00001,0.0\n" +
        "NEW-OMM,2026-002A,2026-10-08T00:00:00.000000,15.50000000,0.0010000,51.6000,120.0000,80.0000,25.0000,0,U,100002,999,12345,0.0001,0.00001,0.0\n";
}
