// PhaseA2ValidationRules.js: an original script for checking that
// FiddlerScript runs on live traffic (tools/Violens.FiddlerScriptDemo does
// the same offline).
//
// To use it: load this file in the FiddlerScript panel, start capturing
// and browse (with VioLens's root trusted). Each numbered comment says what
// to look for.

class Handlers {
    static function OnBeforeRequest(oSession: Session) {
        // (1) OnBeforeRequest ran. Look for these two headers in Request >
        //     Headers. X-Violens-Session-Id should match the row's "#" (or be
        //     close to it under concurrent traffic; the id is a best guess).
        oSession.oRequest.headers["X-Violens-Phase-A2"] = "OnBeforeRequest-ran";
        oSession.oRequest.headers["X-Violens-Session-Id"] = String(oSession.id);

        // (2) An edited request reaches the server: a marker is appended to
        //     POST/PUT/PATCH bodies. Send one to an echo service such as
        //     https://httpbin.org/post and the marker comes back.
        if (oSession.HTTPMethodIs("POST") || oSession.HTTPMethodIs("PUT") || oSession.HTTPMethodIs("PATCH")) {
            var originalBody: String = System.Text.Encoding.UTF8.GetString(oSession.requestBodyBytes);
            oSession.requestBodyBytes = System.Text.Encoding.UTF8.GetBytes(
                originalBody + "\n[violens-phase-a2-request-edit]");
        }
    }

    static function OnBeforeResponse(oSession: Session) {
        // (3) OnBeforeResponse ran: check Response > Headers.
        oSession.oResponse.headers["X-Violens-Phase-A2"] = "OnBeforeResponse-ran";

        // (4) A response body edit reaches the browser: HTML responses are
        //     decoded (zstd isn't supported) and get a comment before </body>,
        //     or at the end. View Source and search for
        //     "violens-phase-a2-response-edit".
        if (oSession.oResponse.headers.ExistsAndContains("Content-Type", "text/html")) {
            oSession.utilDecodeResponse();

            var replacements: int = oSession.utilReplaceInResponse(
                "</body>", "<!-- violens-phase-a2-response-edit --></body>");

            if (replacements == 0) {
                var text: String = System.Text.Encoding.UTF8.GetString(oSession.responseBodyBytes);
                oSession.utilSetResponseBody(text + "\n<!-- violens-phase-a2-response-edit -->");
            }
        }
    }
}
