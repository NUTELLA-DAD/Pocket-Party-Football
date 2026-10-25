using System.Collections;
using System.IO;
using UnityEngine;

namespace PocketFootball.Boot
{
    // Waits a fixed number of frames (no wall-clock), saves <dir>/<fixture>.png, optionally quits.
    public sealed class ScreenshotHarness : MonoBehaviour
    {
        const int SettleFrames = 30;
        public string Dir, Fixture;
        public bool QuitAfter;

        IEnumerator Start()
        {
            for (int i = 0; i < SettleFrames; i++) yield return null;
            yield return new WaitForEndOfFrame();
            Directory.CreateDirectory(Dir);
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            string path = Path.Combine(Dir, Fixture + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Debug.Log("[shots] wrote " + path);
            if (QuitAfter) Application.Quit(0);
        }
    }
}
