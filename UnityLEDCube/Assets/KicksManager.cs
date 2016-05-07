using UnityEngine;
using System.Collections;

public class KicksManager : MonoBehaviour {

	// Use this for initialization
	void Start () {
	
	}
	
	// Update is called once per frame
	void Update () {
	
	}

	public void Play() {
		Component[] children = GetComponentsInChildren<ParticleSystem>();
		foreach (ParticleSystem childParticleSystem in children){
			if (childParticleSystem.name.StartsWith("Kick"))
			childParticleSystem.Emit (1);
		}
	}
}
