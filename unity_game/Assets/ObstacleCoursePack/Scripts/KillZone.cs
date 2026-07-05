using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class KillZone : MonoBehaviour
{
    void OnTriggerEnter(Collider col)
    {
        if (col.gameObject.tag == "Player")
        {
			// GetComponentInParent + null guard: while ragdolled the body's bone colliders are also tagged
			// "Player" but the CharacterControls lives on the root, so resolve up the hierarchy.
			CharacterControls cc = col.GetComponentInParent<CharacterControls>();
			if (cc == null) return;
			if (WebBridge.Multiplayer)
			{
				// Multiplayer (Spinner): first-death survival. Report how long we lasted and stop; no respawn.
				MatchReporter.ReportResult(MatchReporter.CurrentMode(), MatchClock.ElapsedMs, false);
				cc.LockControl(true);
			}
			else
			{
				cc.LoadCheckPoint(); // single-player: respawn at checkpoint
			}
		}
	}
}
