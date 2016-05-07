using UnityEngine;
using System.Collections;

public class CircleFountainManager : MonoBehaviour {

	private Component[] fountains;
	public int Mode;

	public float maxAngle;
	public float minAngle;
	private int direction;

	public float maxTime;
	private float curTime;

	private Vector3 [] rotate = new Vector3[] {
		Vector3.left, Vector3.right, Vector3.right, Vector3.left,
		Vector3.up, Vector3.down, Vector3.down, Vector3.up};

	// Use this for initialization
	void Start () {
		fountains = GetComponentsInChildren<ParticleSystem>();
		direction = 0;
		curTime = 0;

		Play ();

		foreach (ParticleSystem child in fountains) {
			child.Play();
		}
	}

	// Update is called once per frame
	void Update () {
		Play ();
	}

	public void Play () {
		int count = 0;

		foreach (ParticleSystem child in fountains) {
			child.transform.Rotate (rotate[direction] * 5f * Time.deltaTime);
			child.startSpeed += 3f * Time.deltaTime;
			child.startLifetime += 0.05f * Time.deltaTime;

			count++;
		}

		curTime += Time.deltaTime;
		if (curTime > maxTime) {
			direction = (direction + 1) % 8;
			curTime = 0;
		}
	}
}
