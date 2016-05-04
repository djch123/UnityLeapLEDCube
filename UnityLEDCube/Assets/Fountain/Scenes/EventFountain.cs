using UnityEngine;
using System.Collections;

public class EventFountain : MonoBehaviour {

    public GameObject AudioBeat;

	public ParticleSystem fountainkick;
	public ParticleSystem fountainsnare;
	public ParticleSystem fountainhithat;
	public ParticleSystem fountainenergy;

    public void MyCallbackEventHandler(BeatDetection.EventInfo eventInfo)
    {
        switch(eventInfo.messageInfo)
        {
		case BeatDetection.EventType.Energy:
			fountainenergy.Play ();
//                StartCoroutine(showText(energy, genergy));
                break;
		case BeatDetection.EventType.HitHat:
			fountainhithat.Play ();
//                StartCoroutine(showText(hithat, ghithat));
                break;
		case BeatDetection.EventType.Kick:
			fountainkick.Play ();
//                StartCoroutine(showText(kick, gkick));
                break;
		case BeatDetection.EventType.Snare:
			fountainsnare.Play ();
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
