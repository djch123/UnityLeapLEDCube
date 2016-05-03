using UnityEngine;
using System.Collections;

public class PlayerController : MonoBehaviour {

	// Declare your FMOD Sounds in this section

    public float speed;

    Rigidbody rigidBody;                                //rigid body component

   [FMODUnity.EventRef]
	public string music = "event:/Tune";
    FMOD.Studio.EventInstance musicEv;                  //cube event music
    FMOD.Studio.ParameterInstance musicEndParam;        //end param object (for transitioning to the end of music)

	[FMODUnity.EventRef]
	public string rolling = "event:/Rolling";
    FMOD.Studio.EventInstance rollingEv;                //rolling event
    FMOD.Studio.ParameterInstance rollingSpeedParam;    //speed param object

	[FMODUnity.EventRef]
	public string inputSound = "event:/Input_1";

	[FMODUnity.EventRef]
	public string reverbSnapshot = "snapshot:/Reverb";
    FMOD.Studio.EventInstance reverbSnapshotEv;

    void Start ()
	{
        rigidBody = GetComponent<Rigidbody>();

		// Create FMOD event instances and get parameters in this section

        musicEv = FMODUnity.RuntimeManager.CreateInstance(music);
        musicEv.getParameter("end", out musicEndParam);

        rollingEv = FMODUnity.RuntimeManager.CreateInstance(rolling);
        rollingEv.getParameter("speed", out rollingSpeedParam);
        rollingEv.start();

        reverbSnapshotEv = FMODUnity.RuntimeManager.CreateInstance(reverbSnapshot);
    }

    void FixedUpdate ()
	{
        //player movement with input axis
		float moveHorizontal = Input.GetAxis("Horizontal");
		float moveVertical = Input.GetAxis("Vertical");

        Vector3 movement = new Vector3(moveHorizontal, 0.0f, moveVertical);

        //apply impulse, clamp by max and apply velocity

        rigidBody.AddForce(movement * speed, ForceMode.Force);

		//calculate speed of ball rolling, assign that to the rolling event parameter

        rollingSpeedParam.setValue(Mathf.Max(Mathf.Abs(rigidBody.velocity.x), Mathf.Abs(rigidBody.velocity.z)) * 40.0f);

	}

	void Update ()
	{
		// Detect spacebar press

		if (Input.GetKeyDown ("space")) {
		
			FMODUnity.RuntimeManager.PlayOneShot (inputSound);

		}
	}


    void OnTriggerEnter(Collider other)
	{
        /* if colliding with cubes */

		if (other.gameObject.CompareTag ("Pickup"))
        {
			other.gameObject.SetActive(false);
		}

		if (other.gameObject.CompareTag ("Playcube")) {
			// When collision with the Playcube is detected, and if not already playing music event, play the music

            FMOD.Studio.PLAYBACK_STATE play_state;
            musicEv.getPlaybackState(out play_state);
            if (play_state != FMOD.Studio.PLAYBACK_STATE.PLAYING)
            {
				musicEndParam.setValue(0);
                musicEv.start();
            }
		}

		if (other.gameObject.CompareTag ("Stopcube"))
        {
			// When collision with the Stopcube is detected, set end param to 1, which transitions to the end of the event music

            musicEndParam.setValue(1);

			// musicEv.release (); if you do not intend on playing this sound again
		}

		if (other.gameObject.CompareTag ("Killcube"))
        {
			// When collision with the Killcube is detected, immediately stop the event music

            musicEv.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);

			//musicEv.release (); can be called if the music does not need to be switched on again.
		}

        if (other.gameObject.CompareTag("ReverbZone"))
        {
	
			// When collision with the ReverbZone is detected, turn on the Reverb Snapshot

            reverbSnapshotEv.start();

            Debug.Log("reverb snapshot begin");
        }
	}

    void OnTriggerExit(Collider other)
    {
        if (other.gameObject.CompareTag("ReverbZone"))
        {
			// When collision with the ReverbZone ends, turn off the Reverb Snapshot

            reverbSnapshotEv.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);

            Debug.Log("reverb snapshot stopped");
        }
    }
}