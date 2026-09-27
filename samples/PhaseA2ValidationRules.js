// PhaseA2ValidationRules.js -- an original script written to validate
// CLeARINET's Phase A2 FiddlerScript listener wiring: InterceptingProxyListener
// actually calling Handlers.OnBeforeRequest/OnBeforeResponse against real,
// live proxied traffic, not just the offline Clearinet.FiddlerScriptDemo
// smoke test (see tools/Clearinet.FiddlerScriptDemo/SampleCustomRules.js for
// that one). Not copied from Fiddler Classic's own SampleRules.js or any
// cookbook -- written fresh for this validation pass.
//
// How to use this: open the desktop app, paste this file's path into the
// FiddlerScript panel, click Load, then Start the proxy and browse through
// it (with CLeARINET's root CA trusted, same as any other capture session).
// Each numbered comment below says exactly what to look for and where.
//
// Deliberately sticks to things the desktop app can already show you today:
// the Request/Response Headers inspector, the Raw/Text inspectors, and the
// session grid's own "#" column. It does NOT use oSession["ui-color"] or
// oSession["ui-customcolumn"] -- those are recorded on Exchange, but
// nothing in the UI renders either one yet (that's Phase D, still unbuilt
// -- see the FiddlerScript Compatibility Design doc's own phase list).

class Handlers {
    static function OnBeforeRequest(oSession: Session) {
        // (1) Proves OnBeforeRequest ran for THIS exchange, and shows the
        //     best-effort session id SessionStore.PeekNextId() handed it.
        //     Open the Request > Headers inspector for any captured session
        //     and look for these two headers. X-Clearinet-Session-Id should
        //     match (or be very close to, under concurrent traffic -- see
        //     SessionStore.PeekNextId's own remarks on why it's best-effort,
        //     not guaranteed exact) that row's own "#" column in the grid.
        oSession.oRequest.headers["X-Clearinet-Phase-A2"] = "OnBeforeRequest-ran";
        oSession.oRequest.headers["X-Clearinet-Session-Id"] = String(oSession.id);

        // (2) Proves an EDITED request actually reaches the real server,
        //     not just the local, throwaway Exchange copy: appends a marker
        //     to the body of any POST/PUT/PATCH. Point a request at an
        //     endpoint that echoes its own request body back in the
        //     response (https://httpbin.org/post is a public one) and
        //     you'll see the marker there too -- proof the edited body, not
        //     the original, is what actually went out over the wire.
        if (oSession.HTTPMethodIs("POST") || oSession.HTTPMethodIs("PUT") || oSession.HTTPMethodIs("PATCH")) {
            var originalBody: String = System.Text.Encoding.UTF8.GetString(oSession.requestBodyBytes);
            oSession.requestBodyBytes = System.Text.Encoding.UTF8.GetBytes(
                originalBody + "\n[clearinet-phase-a2-request-edit]");
        }
    }

    static function OnBeforeResponse(oSession: Session) {
        // (3) Proves OnBeforeResponse ran, on every single response
        //     regardless of content type -- check Response > Headers.
        oSession.oResponse.headers["X-Clearinet-Phase-A2"] = "OnBeforeResponse-ran";

        // (4) Proves a response BODY edit actually reaches the client: for
        //     a text/html response, decodes it first (undoes gzip/br/
        //     deflate, so the edit below lands in the actual bytes the
        //     browser reads -- see utilDecodeResponse's own remarks on the
        //     one encoding it doesn't handle, zstd) and inserts a visible
        //     HTML comment right before the closing </body> tag, or
        //     appends it if there isn't one, so this still proves
        //     something either way. Load any plain HTML page through the
        //     proxy, View Source, and search for
        //     "clearinet-phase-a2-response-edit".
        if (oSession.oResponse.headers.ExistsAndContains("Content-Type", "text/html")) {
            oSession.utilDecodeResponse();

            var replacements: int = oSession.utilReplaceInResponse(
                "</body>", "<!-- clearinet-phase-a2-response-edit --></body>");

            if (replacements == 0) {
                var text: String = System.Text.Encoding.UTF8.GetString(oSession.responseBodyBytes);
                oSession.utilSetResponseBody(text + "\n<!-- clearinet-phase-a2-response-edit -->");
            }
        }
    }
}
