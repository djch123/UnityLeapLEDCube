﻿/*
 * Copyright (c) 2015 Allan Pichardo
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *  http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using UnityEngine;
using System;

/*
 * Make your class implement the interface AudioProcessor.AudioCallbaks
 */
public class Example : MonoBehaviour, AudioProcessor.AudioCallbacks
{
	private bool big;

    void Start()
    {
        //Select the instance of AudioProcessor and pass a reference
        //to this object
        AudioProcessor processor = FindObjectOfType<AudioProcessor>();
        processor.addAudioCallback(this);
		big = false;
    }

    
    void Update()
    {
		if (!big) {
			transform.localScale = new Vector3 (1F, 2F, 1F);
		} else {
			big = false;
		}
    }

    //this event will be called every time a beat is detected.
    //Change the threshold parameter in the inspector
    //to adjust the sensitivity
    public void onOnbeatDetected()
    {
		Debug.Log("Beat!!!");
		big = true;
		transform.localScale = new Vector3(1F, 5F, 1F);
    }

    //This event will be called every frame while music is playing
    public void onSpectrum(float[] spectrum)
    {
        //The spectrum is logarithmically averaged
        //to 12 bands

        for (int i = 0; i < spectrum.Length; ++i)
        {
            Vector3 start = new Vector3(i, 0, 0);
            Vector3 end = new Vector3(i, spectrum[i] * 10 , 0);
            Debug.DrawLine(start, end);
        }
    }
}
