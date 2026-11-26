"""Loopback SOAP camera fixture paired with the real RTSP/RTP source."""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from html import escape
import sys
import xml.etree.ElementTree as ET

rtsp = Path(sys.argv[1]).read_text().strip()

class Camera(BaseHTTPRequestHandler):
    def do_POST(self):
        body = ET.fromstring(self.rfile.read(int(self.headers["Content-Length"])))
        soap = body.find("{http://www.w3.org/2003/05/soap-envelope}Body")
        if soap is None:
            soap = body.find("{http://schemas.xmlsoap.org/soap/envelope/}Body")
        operation = next(iter(soap)).tag.rsplit("}", 1)[-1]
        endpoint = f"http://127.0.0.1:{self.server.server_port}/onvif"
        responses = {
            "GetServices": f"<Service><Namespace>http://www.onvif.org/ver20/media/wsdl</Namespace><XAddr>{endpoint}</XAddr></Service>",
            "GetProfiles": '<Profiles token="primary"><Name>Browser camera</Name></Profiles>',
            "GetStreamUri": f"<Uri>{escape(rtsp)}</Uri>",
        }
        if operation not in responses:
            self.send_error(400, operation)
            return
        result = (f'<s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"><s:Body>'
                  f'<{operation}Response>{responses[operation]}</{operation}Response>'
                  '</s:Body></s:Envelope>').encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/soap+xml")
        self.send_header("Content-Length", str(len(result)))
        self.end_headers()
        self.wfile.write(result)

with ThreadingHTTPServer(("127.0.0.1", 0), Camera) as server:
    Path(sys.argv[2]).write_text(f"http://127.0.0.1:{server.server_port}/onvif")
    server.serve_forever()
