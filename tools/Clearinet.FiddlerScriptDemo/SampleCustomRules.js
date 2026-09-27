// SampleCustomRules.js -- an original demo script for CLeARINET's
// FiddlerScript engine, written for this smoke-test tool. Not copied from
// Fiddler Classic's own SampleRules.js or any cookbook -- see
// Clearinet.FiddlerScriptDemo's own Program.cs for why: a review copy has
// to be safe to look at without checking it against anyone else's
// copyrighted script.
//
// Deliberately touches a handful of different FiddlerScript features in
// one file, the same way a real CustomRules.js accumulates unrelated
// rules over time:
//   - typed var declarations (JScript.NET syntax CLeARINET's preprocessor
//     has to strip before Jint can parse this)
//   - a request-side rule: tag a custom header and a UI color flag
//   - a response-side rule: rewrite the body and use .NET CLR interop
//     (System.Text.StringBuilder) to build a value

class Handlers {
    static function OnBeforeRequest(oSession: Session) {
        oSession.oRequest.headers["X-Clearinet-Demo"] = "OnBeforeRequest ran";

        if (oSession.uriContains("slow-endpoint")) {
            oSession["ui-color"] = "orange";
        }
    }

    static function OnBeforeResponse(oSession: Session) {
        if (oSession.oResponse.headers.ExistsAndContains("Content-Type", "text/plain")) {
            oSession.utilDecodeResponse();

            var replacements: int = oSession.utilReplaceInResponse("placeholder", "replaced-by-fiddlerscript");

            var summary: System.Text.StringBuilder = new System.Text.StringBuilder();
            summary.Append("status=");
            summary.Append(String(oSession.responseCode));
            summary.Append(" replacements=");
            summary.Append(String(replacements));
            oSession["ui-customcolumn"] = summary.ToString();
        }
    }
}
