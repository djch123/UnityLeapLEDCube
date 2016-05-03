using UnityEngine;
using System.Collections;

public class hitSound : MonoBehaviour {

	[FMODUnity.EventRef]
	public string impactSound = "Event:/ImpactSound";
	FMOD.Studio.EventInstance impactEv;
	FMOD.Studio.ParameterInstance Intensity;

	public Rigidbody rb;

	public float HitVel;

	// Use this for initialization
	void Start () {

		rb = GetComponent<Rigidbody>();

		impactEv = FMODUnity.RuntimeManager.CreateInstance(impactSound);

		impactEv.getParameter("Intensity", out Intensity);
	
	}
	
	// Update is called once per frame
	void OnCollisionEnter(Collision collision) {	

		HitVel = (collision.relativeVelocity.magnitude);
		
		HitVel = HitVel/20;
		Debug.Log (HitVel);
		
		Intensity.setValue (HitVel);
		
		impactEv.start ();

			if (collision.gameObject.CompareTag ("Ground")) {

				Debug.Log ("ground");

			}


			else {

				Debug.Log ("cube");

			}

	}
}