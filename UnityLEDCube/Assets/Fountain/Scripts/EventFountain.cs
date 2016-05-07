using UnityEngine;
using System.Collections;

public class EventFountain : MonoBehaviour {

    public GameObject AudioBeat;

	public GameObject FountainEnergy;
	public KicksManager FireworkKicks;
	public FountainLineManager LineFountains;

    public void MyCallbackEventHandler(BeatDetection.EventInfo eventInfo)
    {
		Component[] children;
        switch(eventInfo.messageInfo)
        {
		case BeatDetection.EventType.Energy:
			children = FountainEnergy.GetComponentsInChildren<ParticleSystem>();
			foreach (ParticleSystem childParticleSystem in children){
				childParticleSystem.Play ();
			}
//                StartCoroutine(showText(energy, genergy));
                break;
		case BeatDetection.EventType.HitHat:
//                StartCoroutine(showText(hithat, ghithat));
                break;
		case BeatDetection.EventType.Kick:
//                StartCoroutine(showText(kick, gkick));
                break;
		case BeatDetection.EventType.Snare:
			FireworkKicks.Play ();
//                StartCoroutine(showText(snare, gsnare));
                break;
        }
    }

    // Use this for initialization
    void Start () {
        //Register the beat callback function
        AudioBeat.GetComponent<BeatDetection>().CallBackFunction = MyCallbackEventHandler;
    }

}
