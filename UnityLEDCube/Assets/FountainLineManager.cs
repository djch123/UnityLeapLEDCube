using UnityEngine;
using System.Collections;

public class FountainLineManager : MonoBehaviour {
	private Component[] fountains;
	private int index;
	public int Mode;

	private int i;

//	static float [][] rotation = {{1f, -1f, 1f, -1f}, {-1f, 1f, 1f, -1f}};

	// Use this for initialization
	void Start () {
		fountains = GetComponentsInChildren<ParticleSystem>();
		index = 0;
		i = 0;

		Play ();

		foreach (ParticleSystem child in fountains) {
			child.Play();
		}
	}
	
	// Update is called once per frame
	void Update () {
		i++;
		if (i % 15 == 0) {
			Play ();
		}

		if (i == 330) {
			setMode ((Mode + 1) % 4);
			i = 0;
		}
	}

	public void Play () {
		switch (Mode) {
		case 0:
			{
				break;
			}

		case 1:
			{
				int count = 0;
				foreach (ParticleSystem child in fountains) {
					float tmp = Mathf.Abs ((index + count) % 19 - 9) + 5f;

					child.startSpeed = tmp * 10;
					count++;
				}
				index++;
				break;
			}

		case 2:
			{
				(fountains[index % 19] as ParticleSystem).Emit (1000);
				index++;
				break;
			}

		case 3:
			{
				int count = 0;
				foreach (ParticleSystem child in fountains) {
					float tmp = Mathf.Abs ((index + count) % 19 - 9) - 4.5f;
					tmp /= 3;

					child.transform.Rotate (Vector3.right * tmp);

					count++;
				}
				index++;
				break;
			}
		}
	}

	public void setMode (int mode) {
		Mode = mode;
		index = 0;
		foreach (ParticleSystem child in fountains) {
			child.Stop ();
		}
		init ();
	}

	void init(){
		switch (Mode) {
		case 0:
			foreach (ParticleSystem child in fountains) {
				child.loop = true;
				var shape = child.shape;
				shape.enabled = true;
				shape.angle = 1f;
				shape.arc = 2f;
				child.startSpeed = 100f;

				child.Play ();
			}
			break;

		case 1:
			foreach (ParticleSystem child in fountains) {
				child.loop = true;
				var shape = child.shape;
				shape.enabled = true;
				shape.angle = 1f;
				shape.arc = 2f;

				child.Play ();
			}
			break;

		case 2:
			{
				foreach (ParticleSystem child in fountains) {
					child.loop = false;
					var shape = child.shape;
					shape.enabled = true;
					shape.angle = 5F;
					shape.arc = 0.1F;
					child.startSpeed = 100f;
				}
				break;
			}

		case 3:
			{
				foreach (ParticleSystem child in fountains) {
					child.loop = true;
					var shape = child.shape;
					shape.enabled = true;
					shape.angle = 1f;
					shape.arc = 2f;
					child.startSpeed = 100f;

					child.Play ();
				}
				break;
			}
		}
	}
}
