﻿/******************************************************
 * Attach this script to the main camera in the scene
 ******************************************************/

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using MetavoxelEngine;

namespace MetavoxelEngine
{
    /*
     * Type definitions for metavoxel & particle data
     */

    // The world is divided into metavoxels, each of which is made up of voxels.
    // This metavoxel grid is oriented to face the light and each metavoxel has a list of
    // the particles it covers
    struct MetaVoxel
    {
        public Vector3 mPos;
        public List<ParticleSystem.Particle> mParticlesCovered;
        public bool mCleared;
    }

    // When "filling a metavoxel" using the "Fill Volume" shader, we send per-particle-info
    // for use in the voxel-particle coverage test
    struct DisplacedParticle
    {
        public Matrix4x4 mWorldToLocal;
        public Vector3 mWorldPos;
        public float mRadius; // world units
        public float mOpacity;
    }

    // Helps sort metavoxels of a Z-Slice from the eye
    public struct MetavoxelSortData : IComparable<MetavoxelSortData>
    {
        public int x, y;
        public float distance;

        public MetavoxelSortData(int xx, int yy, float dd)
        {
            x = xx; y = yy; distance = dd;
        }

        public void Print(int index)
        {
            Debug.Log("At index " + index + "(" + x + "," + y + ") at disance " + distance);
        }

        public int CompareTo(MetavoxelSortData other)
        {
            return this.distance.CompareTo(other.distance);
        }
    }


    [RequireComponent(typeof(Camera))]
    public class VolumetricParticleRenderer : MonoBehaviour
    {
        /*
         *  public variables to be edited in the inspector window
         */
        /********************* game objects/components that need to be set *************************/
        public Light dirLight;
        public ParticleSystem particleSys;
        public Material matFillVolume;
        public Material matRayMarch;
        public Material matBlendParticles; // material to blend the raymarched volume with the camera's RT    
        public Material mvLineColor;
        public Shader generateLightDepthMapShader;
        public GameObject gridCenter;

        /********************** metavoxel layout/size **********************************************/
        public int numMetavoxelsX, numMetavoxelsY, numMetavoxelsZ; // # metavoxels in the grid along x, y & z
        public Vector3 mvScale;// size of a metavoxel in world units
        public int numVoxelsInMetavoxel; // affects the size of the 3D texture used to fill a metavoxel
        public int numBorderVoxels; // per end (i.e. a value of 1 means 2 voxels per dimension are border voxels)    

        /********************** rendering vars **********************************************/
        public int updateInterval;        
        public int rayMarchSteps;
        public Vector3 ambientColor;        
        public int volumeTextureAnisoLevel;

            /*** gui controls ****/
        public float fDisplacementScale;
        public bool fadeOutParticles;
        private bool bShowMetavoxelGrid;
        private bool bShowRayMarchSamplesPerPixel;
        private bool bShowMetavoxelDrawOrder;
        private bool bShowRayMarchBlendFunc;
        public float opacityFactor;
        public int softParticleStepDistance;

        /*
        * private members 
        */
        // Metavoxel grid state
        private MetaVoxel[, ,] mvGrid;
        private Vector3 wsGridCenter;
        private Vector3 mvScaleWithBorder;

        // Render target resources
        private RenderTexture mainSceneRT; // camera draws all the objects in the scene but for the particles into this
        private RenderTexture particlesRT; // result of raymarching the metavoxels is stored in this    
        private RenderTexture fillMetavoxelRT, fillMetavoxelRT1; // bind this as RT when filling a metavoxel. we don't sample/use it though..
        private RenderTexture[, ,] mvFillTextures; // 3D textures that hold metavoxel fill data
        public RenderTexture lightPropogationUAV; // Light propogation texture used while filling metavoxels
        public RenderTexture lightDepthMap;

        // scripted camera for mini-shadow map generation
        private GameObject lightCamera;

        // misc state
        //private AABBForParticles pBounds;
        private int numParticlesEmitted;
        private int numMetavoxelsCovered;
        private Quaternion lightOrientation; // Light movement detection
        private Mesh cubeMesh;
        private Mesh quadMesh;


        //-------------------------------  Unity callbacks ----------------------------------------
        void Start()
        {
            fadeOutParticles = false;
            volumeTextureAnisoLevel = 1; // The value range of this variable goes from 1 to 9, where 1 equals no filtering applied and 9 equals full filtering applied
            lightOrientation = dirLight.transform.rotation;
            numMetavoxelsCovered = 0;
            wsGridCenter = Vector3.zero;
            mvScaleWithBorder = mvScale * numVoxelsInMetavoxel / (numVoxelsInMetavoxel - 2 * numBorderVoxels);

            CreateResources();
            CreateMeshes();
            InitCameraAtLight();

            // [perf threat] Unity is going to do a Z-prepass simply because of the line below
            this.GetComponent<Camera>().depthTextureMode = DepthTextureMode.Depth; // this makes the depth buffer available for all the shaders as _CameraDepthTexture
            
            //pBounds = particleSys.GetComponent<AABBForParticles>();        
        }


        void Update()
        {
        }


        //Order of event calls in unity: file:///C:/Program%20Files%20(x86)/Unity/Editor/Data/Documentation/html/en/Manual/ExecutionOrder.html
        //[Render objects in scene] -> [OnRenderObject] -> [OnPostRender] -> [OnRenderImage]

        //OnRenderObject is called after camera has rendered the scene.
        //This can be used to render your own objects using Graphics.DrawMeshNow or other functions.
        //This function is similar to OnPostRender, except OnRenderObject is called on any object that has a script with the function; no matter if it's attached to a Camera or not.
        //void OnRenderObject()
        //{

        //}

        void OnPreRender()
        {
            // Since we don't directly render to the back buffer, we need to clear the render targets used every frame.
            RenderTexture.active = particlesRT;
            GL.Clear(false, true, new Color(0f, 0f, 0f, 0f));

            RenderTexture.active = mainSceneRT;
            GL.Clear(true, true, Color.black);
            GetComponent<Camera>().targetTexture = mainSceneRT;
        }


        //// OnPostRender is called after a camera has finished rendering the scene.
        void OnPostRender()
        {
            // Generate the depth map from the light's pov
            lightCamera.GetComponent<Camera>().RenderWithShader(generateLightDepthMapShader, null as string);

            if (Time.frameCount % updateInterval == 0)
            {
                if (dirLight.transform.rotation != lightOrientation /* light direction has changed*/ ||
                    wsGridCenter != gridCenter.transform.position)
                {
                    lightOrientation = dirLight.transform.rotation;
                    wsGridCenter = gridCenter.transform.position;
                    UpdateMetavoxelPositions();
                    UpdatePositionOfCameraAtLight();
                }

                BinParticlesToMetavoxels();
                FillMetavoxels();
            }

            // Use the camera's existing depth buffer to depth-test the particles, while
            // writing the ray marched volume into a separate color buffer that's blended
            // with the main scene in OnRenderImage(..)
            Graphics.SetRenderTarget(particlesRT.colorBuffer, mainSceneRT.depthBuffer);

            // fill particlesRT with the ray marched volume (the loop is per directional light source)
            RenderMetavoxels();

            // blend the particles onto the main (opaque) scene. [todo] what happens to billboarded particles on the main scene? when're they rendered?
            Graphics.Blit(particlesRT, mainSceneRT, matBlendParticles);

            if (bShowMetavoxelGrid)
            {
                DrawMetavoxelGrid();
            }

            // need to set the targetTexture to null, else the Blit doesn't work
            GetComponent<Camera>().targetTexture = null;
            Graphics.Blit(mainSceneRT, null as RenderTexture); // copy to back buffer
        }


        //-------------------------------  private fns --------------------------------------------
        void CreateResources()
        {
            if (!particlesRT)
            {
                particlesRT = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
                particlesRT.useMipMap = false;
                particlesRT.isVolume = false;
                particlesRT.enableRandomWrite = false;
                particlesRT.Create();
            }

            if (!mainSceneRT)
            {
                mainSceneRT = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
                mainSceneRT.useMipMap = false;
                mainSceneRT.isVolume = false;
                mainSceneRT.enableRandomWrite = false;
                mainSceneRT.Create();
                GetComponent<Camera>().targetTexture = mainSceneRT;
            }

            if (!fillMetavoxelRT)
            {
                fillMetavoxelRT = new RenderTexture(numVoxelsInMetavoxel, numVoxelsInMetavoxel, 0, RenderTextureFormat.R8);
                fillMetavoxelRT.useMipMap = false;
                fillMetavoxelRT.isVolume = false;
                fillMetavoxelRT.enableRandomWrite = false;
                fillMetavoxelRT.Create();
            }

            if (!fillMetavoxelRT1)
            {
                fillMetavoxelRT1 = new RenderTexture(numVoxelsInMetavoxel, numVoxelsInMetavoxel, 0, RenderTextureFormat.R8);
                fillMetavoxelRT1.useMipMap = false;
                fillMetavoxelRT1.isVolume = false;
                fillMetavoxelRT1.enableRandomWrite = false;
                fillMetavoxelRT1.Create();
            }


            if (!lightPropogationUAV)
            {
                lightPropogationUAV = new RenderTexture(numMetavoxelsX * numVoxelsInMetavoxel, numMetavoxelsY * numVoxelsInMetavoxel, 0 /* no need depth surface, just color*/, RenderTextureFormat.RFloat);
                lightPropogationUAV.generateMips = false;
                lightPropogationUAV.enableRandomWrite = true; // use as UAV
                lightPropogationUAV.Create();
            }

            if (!lightDepthMap)
            {
                lightDepthMap = new RenderTexture(numMetavoxelsX * numVoxelsInMetavoxel, numMetavoxelsY * numVoxelsInMetavoxel, 24, RenderTextureFormat.Depth);
                lightDepthMap.generateMips = false;
                lightDepthMap.enableRandomWrite = false;
                lightDepthMap.Create();
            }
           
            CreateMetavoxelGrid(); // creates the fill texture per metavoxel
        }


        // Create mv info & associated fill texture for every mv in the grid
        void CreateMetavoxelGrid()
        {
            mvGrid = new MetaVoxel[numMetavoxelsZ, numMetavoxelsY, numMetavoxelsX]; // note index order -- prefer locality in X,Y
            mvFillTextures = new RenderTexture[numMetavoxelsZ, numMetavoxelsY, numMetavoxelsX];

            // Init fill texture and particle list per metavoxel
            for (int zz = 0; zz < numMetavoxelsZ; zz++)
            {
                for (int yy = 0; yy < numMetavoxelsY; yy++)
                {
                    for (int xx = 0; xx < numMetavoxelsX; xx++)
                    {
                        mvGrid[zz, yy, xx].mParticlesCovered = new List<ParticleSystem.Particle>();
                        CreateFillTexture(xx, yy, zz);
                    }
                }
            }

            // Find metavoxel position & orientation 
            UpdateMetavoxelPositions();
        }


        void CreateFillTexture(int xx, int yy, int zz)
        {
            // Note that constructing a RenderTexture object does not create the hardware representation immediately. The actual render texture is created upon first use or when Create is called manually
            // file:///C:/Program%20Files%20(x86)/Unity/Editor/Data/Documentation/html/en/ScriptReference/RenderTexture-ctor.html
            mvFillTextures[zz, yy, xx] = new RenderTexture(numVoxelsInMetavoxel, numVoxelsInMetavoxel, 0 /* no need depth surface, just color*/, RenderTextureFormat.ARGBHalf);
            mvFillTextures[zz, yy, xx].isVolume = true;
            mvFillTextures[zz, yy, xx].volumeDepth = numVoxelsInMetavoxel;
            mvFillTextures[zz, yy, xx].generateMips = false;
            mvFillTextures[zz, yy, xx].enableRandomWrite = true; // use as UAV
        }
   
        
        void InitCameraAtLight()
        {
            lightCamera = new GameObject();
            lightCamera.name = "LightCamera";
            lightCamera.transform.parent = dirLight.transform;
            // make the shadow map camera look at the center of the metavoxel grid
            UpdatePositionOfCameraAtLight();

            lightCamera.gameObject.SetActive(false);
          
            Camera c = lightCamera.AddComponent<Camera>() as Camera;
            if (c != null)
            {
                c.orthographic = true;

                // Set the camera extents to that of the metavoxel grid (vertically.. horizontal part is taken care of by aspect ratio)
                // setting ortho size below wont work, since l,r are t,b scaled by aspect ratio. we need a tight fit to the metavoxel grid
                // so we need to explicitly generate the projection matrix as we need it
                //c.orthographicSize = numMetavoxelsY * mvScale.y * 0.5f; // Camera's half-vertical-size when in orthographic mode.

                float r = numMetavoxelsX * mvScale.x * 0.5f, t = numMetavoxelsY * mvScale.y * 0.5f;

                Matrix4x4 orthoProjectionMatrix = Matrix4x4.Ortho(-r, r, -t, t, 0.3f, 1000f);
                c.projectionMatrix = orthoProjectionMatrix; // todo: is this pixel correct? check http://docs.unity3d.com/ScriptReference/GL.LoadPixelMatrix.html

                c.targetTexture = lightDepthMap;
                c.cullingMask = 1 << LayerMask.NameToLayer("Default");
                c.clearFlags = CameraClearFlags.Depth | CameraClearFlags.Color;
                c.useOcclusionCulling = false;
            }
            else
            {
                Debug.LogError("Could not create camera at light");
            }
            // Setting the ortho projection matrix as below doesn't give the expected matrix 
            //c.projectionMatrix = Matrix4x4.Ortho(-wsMetavoxelDimensions.x, wsMetavoxelDimensions.x,
            //                                               -wsMetavoxelDimensions.y, wsMetavoxelDimensions.y,
            //                                               0.3f, 1000);
        }


        void UpdatePositionOfCameraAtLight()
        {
            // ideally we want to set the near plane based on the bounding box of the scene. 
            // for now, keep it at 200 units from the metavoxel center
            lightCamera.transform.position = wsGridCenter - dirLight.transform.forward * 200f;
            lightCamera.transform.localRotation = Quaternion.identity;            
        }
    

        void UpdateMetavoxelPositions()
        {
            /* assumptions:
              i) grid is centered at the world origin
           * ii) effort isn't made to ensure the grid covers the frustum visible from the camera.
           * 
           * the grid layout follows the light (xx=0,yy=0,zz=0) is the left-bottom-front of the grid. that takes care of the position of each metavoxel in the grid.
           * the orientation of each metavoxel however faces the light (X and Z are reversed). this is just confusing. please fix [todo]
          */

            Vector3 lsWorldOrigin = dirLight.transform.worldToLocalMatrix.MultiplyPoint3x4(wsGridCenter); // xform origin to light space

            for (int zz = 0; zz < numMetavoxelsZ; zz++)
            {
                for (int yy = 0; yy < numMetavoxelsY; yy++)
                {
                    for (int xx = 0; xx < numMetavoxelsX; xx++)
                    {
                        Vector3 lsOffset = Vector3.Scale(new Vector3(numMetavoxelsX / 2 - xx, numMetavoxelsY / 2 - yy, numMetavoxelsZ / 2 - zz), mvScale);
                        Vector3 wsMetavoxelPos = dirLight.transform.localToWorldMatrix.MultiplyPoint3x4(lsWorldOrigin - lsOffset); // using - lsOffset means that zz = 0 is closer to the light
                        mvGrid[zz, yy, xx].mPos = wsMetavoxelPos;                       
                    }
                }
            }
        }


        void BinParticlesToMetavoxels()
        {
            // Clear previous particle lists
            for (int zz = 0; zz < numMetavoxelsZ; zz++)
            {
                for (int yy = 0; yy < numMetavoxelsY; yy++)
                {
                    for (int xx = 0; xx < numMetavoxelsX; xx++)
                    {
                        mvGrid[zz, yy, xx].mParticlesCovered.Clear();
                    }
                }
            }


            ParticleSystem.Particle[] parts = new ParticleSystem.Particle[particleSys.maxParticles];
            numParticlesEmitted = particleSys.GetParticles(parts);

            for (int pp = 0; pp < numParticlesEmitted; pp++)
            {
                // xform particle to mv space to make it a sphere-aabb intersection test
                Vector3 wsParticlePos = particleSys.transform.localToWorldMatrix.MultiplyPoint3x4(parts[pp].position);
                Vector3 lsParticlePos = dirLight.transform.worldToLocalMatrix.MultiplyPoint3x4(wsParticlePos);
                Vector3 lsMVGridCenter = dirLight.transform.worldToLocalMatrix.MultiplyPoint3x4(wsGridCenter);

                Vector3 pIndexOffset = (lsParticlePos - lsMVGridCenter) / mvScale.x;
                Vector3 pIndex = pIndexOffset + new Vector3(numMetavoxelsX, numMetavoxelsY, numMetavoxelsZ) * 0.5f;
 
                int pExtents = Mathf.RoundToInt( (parts[pp].size / 2f) / mvScale.x );
                Vector3 minIndex = pIndex - Vector3.one * pExtents, 
                        maxIndex = pIndex + Vector3.one * pExtents;

                Vector3 mvGridIndexLimit = new Vector3(numMetavoxelsX - 1, numMetavoxelsY - 1, numMetavoxelsZ - 1);

                minIndex = Vector3.Max(Vector3.zero, minIndex);
                maxIndex = Vector3.Min(mvGridIndexLimit, maxIndex);

                for (int zz = (int)minIndex.z; zz <= (int)maxIndex.z; zz++)
                {
                    for (int yy = (int)minIndex.y; yy <= (int)maxIndex.y; yy++)
                    {
                        for (int xx = (int)minIndex.x; xx <= (int)maxIndex.x; xx++)
                        {
                            Matrix4x4 worldToMetavoxelMatrix = Matrix4x4.TRS(mvGrid[zz, yy, xx].mPos,
                                                                             lightOrientation,
                                                                             mvScaleWithBorder).inverse; // Account for the border of the metavoxel while binning

                            Vector3 mvParticlePos = worldToMetavoxelMatrix.MultiplyPoint3x4(wsParticlePos);
                            float mvParticleRadius = (parts[pp].size / 2f) / mvScaleWithBorder.x; // Intersection test is with the enlarged metavoxel (i.e. with the border), so 

                            bool particle_intersects_metavoxel = MathUtil.DoesBoxIntersectSphere(new Vector3(-0.5f, -0.5f, -0.5f),
                                                                                                 new Vector3(0.5f, 0.5f, 0.5f),
                                                                                                 mvParticlePos,
                                                                                                 mvParticleRadius);

                            if (particle_intersects_metavoxel)
                                mvGrid[zz, yy, xx].mParticlesCovered.Add(parts[pp]);
                        }
                    }
                }
            } // pp



            //for (int zz = 0; zz < numMetavoxelsZ; zz++)
            //{
            //    for (int yy = 0; yy < numMetavoxelsY; yy++)
            //    {
            //        for (int xx = 0; xx < numMetavoxelsX; xx++)
            //        {
            //            mvGrid[zz, yy, xx].mParticlesCovered.Clear();

            //            Matrix4x4 worldToMetavoxelMatrix = Matrix4x4.TRS(mvGrid[zz, yy, xx].mPos,
            //                                                             lightOrientation,
            //                                                             mvScaleWithBorder).inverse; // Account for the border of the metavoxel while scaling

            //            for (int pp = 0; pp < numParticlesEmitted; pp++)
            //            {
            //                // xform particle to mv space to make it a sphere-aabb intersection test
            //                Vector3 wsParticlePos = particleSys.transform.localToWorldMatrix.MultiplyPoint3x4(parts[pp].position);
            //                Vector3 mvParticlePos = worldToMetavoxelMatrix.MultiplyPoint3x4(wsParticlePos);
            //                float radius = (parts[pp].size / 2f) / mvScaleWithBorder.x;

            //                bool particle_intersects_metavoxel = MathUtil.DoesBoxIntersectSphere(new Vector3(-0.5f, -0.5f, -0.5f),
            //                                                                                        new Vector3(0.5f, 0.5f, 0.5f),
            //                                                                                        mvParticlePos,
            //                                                                                        radius);

            //                if (particle_intersects_metavoxel)
            //                    mvGrid[zz, yy, xx].mParticlesCovered.Add(parts[pp]);
            //            } // pp

            //        } // xx
            //    } // yy
            //} // zz       
        }


        void FillMetavoxels()
        {
            // Clear the light propogation texture before we write to it
            Graphics.SetRenderTarget(lightPropogationUAV);
            GL.Clear(false, true, Color.red);

            SetFillPassConstants();
            numMetavoxelsCovered = 0;

            // process the metavoxels in order of Z-slice closest to light to farthest
            for (int zz = 0; zz < numMetavoxelsZ; zz++)
            {
                for (int yy = 0; yy < numMetavoxelsY; yy++)
                {
                    for (int xx = 0; xx < numMetavoxelsX; xx++)
                    {
                        if (mvGrid[zz, yy, xx].mParticlesCovered.Count != 0)
                        {
                            FillMetavoxel(xx, yy, zz);
                            numMetavoxelsCovered++;
                        }
                    }
                }
            }

        }


        void SetFillPassConstants()
        {
            // metavoxel specific stuff
            matFillVolume.SetVector("_MetavoxelGridDim", new Vector3(numMetavoxelsX, numMetavoxelsY, numMetavoxelsZ));
            matFillVolume.SetFloat("_NumVoxels", numVoxelsInMetavoxel);
            matFillVolume.SetInt("_MetavoxelBorderSize", Mathf.Clamp(numBorderVoxels, 0, numVoxelsInMetavoxel - 2));
            matFillVolume.SetFloat("_MetavoxelScaleZ", mvScaleWithBorder.z);

            // scene related stuff
            // -- light
            matFillVolume.SetTexture("_LightDepthMap", lightDepthMap);
            matFillVolume.SetMatrix("_WorldToLight", lightCamera.transform.worldToLocalMatrix);
            matFillVolume.SetVector("_LightForward", dirLight.transform.forward.normalized);
            matFillVolume.SetVector("_LightColor", dirLight.color);
            matFillVolume.SetVector("_AmbientColor", ambientColor);
            matFillVolume.SetFloat("_InitLightIntensity", 1.0f);
            matFillVolume.SetFloat("_NearZ", lightCamera.GetComponent<Camera>().nearClipPlane);
            matFillVolume.SetFloat("_FarZ", lightCamera.GetComponent<Camera>().farClipPlane);

            Camera c = lightCamera.GetComponent<Camera>();
            matFillVolume.SetMatrix("_LightProjection", c.projectionMatrix);

            // -- particles
            matFillVolume.SetFloat("_OpacityFactor", opacityFactor);
            matFillVolume.SetFloat("_DisplacementScale", fDisplacementScale);
                      
            int fadeParticles = 0;
            if (fadeOutParticles)
                fadeParticles = 1;

            matFillVolume.SetInt("_FadeOutParticles", fadeParticles);
        }


        // Each metavoxel that has particles in it needs to be filled with volume info (opacity for now)
        // Since nothing is rendered to the screen while filling the metavoxel volume textures up, we have to resort to
        void FillMetavoxel(int xx, int yy, int zz)
        {
            //Graphics.ClearRandomWriteTargets();

            // Note: Calling Create lets you create it up front. Create does nothing if the texture is already created.
            // file:///C:/Program%20Files%20(x86)/Unity/Editor/Data/Documentation/html/en/ScriptReference/RenderTexture.Create.html
            if (!mvFillTextures[zz, yy, xx].IsCreated())
                mvFillTextures[zz, yy, xx].Create();

            // don't need to clear the mv fill texture since we write to every pixel on it (on every depth slice)
            // regardless of whether that pixel is covered or not.   
            Graphics.SetRenderTarget(fillMetavoxelRT);
            Graphics.SetRandomWriteTarget(1, mvFillTextures[zz, yy, xx]);
            Graphics.SetRandomWriteTarget(2, lightPropogationUAV);

            // fill structured buffer
            int numParticles = mvGrid[zz, yy, xx].mParticlesCovered.Count;
            DisplacedParticle[] dpArray = new DisplacedParticle[numParticles];

            int index = 0;

            foreach (ParticleSystem.Particle p in mvGrid[zz, yy, xx].mParticlesCovered)
            {
                Vector3 wsPos = particleSys.transform.localToWorldMatrix.MultiplyPoint3x4(p.position);                
                dpArray[index].mWorldToLocal = Matrix4x4.TRS(wsPos, Quaternion.AngleAxis(p.rotation, particleSys.transform.forward), new Vector3(p.size, p.size, p.size)).inverse;
                dpArray[index].mWorldPos = wsPos;
                dpArray[index].mRadius = p.size / 2f;
                dpArray[index].mOpacity = p.lifetime / p.startLifetime; // [time-particle-will-remain-alive / particle-lifetime] use this to make particles less "dense" as they meet their end.
                index++;
            }

            ComputeBuffer dpBuffer = new ComputeBuffer(numParticles,
                                                        Marshal.SizeOf(dpArray[0]));
            dpBuffer.SetData(dpArray);

            // Set material state

            matFillVolume.SetMatrix("_MetavoxelToWorld", Matrix4x4.TRS(mvGrid[zz, yy, xx].mPos,
                                                                       lightOrientation,
                                                                       mvScaleWithBorder)); // need to fill the border voxels of this metavoxel too (so we need to make it "seem" bigger)
            matFillVolume.SetVector("_MetavoxelIndex", new Vector3(xx, yy, zz));
            matFillVolume.SetInt("_NumParticles", numParticles);
            matFillVolume.SetBuffer("_Particles", dpBuffer);
            //matFillVolume.SetPass(0);

            Graphics.Blit(fillMetavoxelRT1, fillMetavoxelRT1, matFillVolume, 0);
            //Graphics.DrawMeshNow(quadMesh, Vector3.zero, Quaternion.identity);
            // cleanup
            dpBuffer.Release();
            Graphics.ClearRandomWriteTargets();
        }


        // The ray march step uses alpha blending and imposes order requirements 
        List<MetavoxelSortData> SortMetavoxelSlicesFarToNearFromEye()
        {
            List<MetavoxelSortData> mvPerSliceFarToNear = new List<MetavoxelSortData>();
            Vector3 cameraPos = Camera.main.transform.position;

            for (int yy = 0; yy < numMetavoxelsY; yy++)
            {
                for (int xx = 0; xx < numMetavoxelsX; xx++)
                {
                    Vector3 distFromEye = mvGrid[0, yy, xx].mPos - cameraPos;
                    //Debug.Log("Distance from mv " + xx + ", " + yy + "to camera is " + Vector3.Dot(distFromEye, distFromEye));
                    mvPerSliceFarToNear.Add(new MetavoxelSortData(xx, yy, Vector3.Dot(distFromEye, distFromEye)));
                }
            }

            mvPerSliceFarToNear.Sort();
            mvPerSliceFarToNear.Reverse();

            return mvPerSliceFarToNear;
        }


        // Submit a cube from the perspective of the main camera
        // This function is called from CameraScript.cs
        public void RenderMetavoxels()
        {
            List<MetavoxelSortData> mvPerSliceFarToNear = SortMetavoxelSlicesFarToNearFromEye();
            SetRaymarchPassConstants();

            Vector3 lsCameraPos = dirLight.transform.worldToLocalMatrix.MultiplyPoint3x4(Camera.main.transform.position);
            float lsFirstZSlice = dirLight.transform.worldToLocalMatrix.MultiplyPoint3x4(mvGrid[0, 0, 0].mPos).z;
            float mvBlendOverIndex = (lsCameraPos.z - lsFirstZSlice) / mvScale.z;

            //float x = -0.6f;
            //Debug.Log("X: " + x + " Rounding x: " + Mathf.RoundToInt(x) + "Ceil(x): " + Mathf.Ceil(x) + " Floor(x): " + Mathf.Floor(x));
            int zBoundary = Mathf.Clamp( Mathf.RoundToInt(mvBlendOverIndex), -1, numMetavoxelsZ - 1);

            int mvCount = 0;

            if (zBoundary >= 0)
            {
                // Render metavoxel slices with back to front blending
                // (a) increasing order along the direction of the light
                // (b) farthest-to-nearest from the camera per slice

                // Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha // Back to Front blending (blend over)
                matRayMarch.SetInt("SrcFactor", (int)UnityEngine.Rendering.BlendMode.One);
                matRayMarch.SetInt("DstFactor", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                matRayMarch.SetInt("SrcFactorA", (int)UnityEngine.Rendering.BlendMode.One);
                matRayMarch.SetInt("DstFactorA", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

                matRayMarch.SetInt("_RayMarchBlendOver", 1);


                for (int zz = 0; zz <= zBoundary; zz++)
                {
                    foreach (MetavoxelSortData vv in mvPerSliceFarToNear)
                    {
                        //Debug.Log("F2N " + mvCount + "(" + vv.x + "," + vv.y + ")");
                        int xx = (int)vv.x, yy = (int)vv.y;

                        if (mvGrid[zz, yy, xx].mParticlesCovered.Count != 0)
                        {
                            RenderMetavoxel(xx, yy, zz, mvCount++);
                        }

                    }
                }
            }

            // Render metavoxel slices
            // (a) increasing order along the direction of the light
            // (b) nearest-to-farthest from the camera per slice
            
			// Blend OneMinusDstAlpha One, OneMinusDstAlpha One // Front to Back blending (blend under)
            matRayMarch.SetInt("SrcFactor", (int)UnityEngine.Rendering.BlendMode.OneMinusDstAlpha);
            matRayMarch.SetInt("DstFactor", (int)UnityEngine.Rendering.BlendMode.One);
            matRayMarch.SetInt("SrcFactorA", (int)UnityEngine.Rendering.BlendMode.OneMinusDstAlpha);
            matRayMarch.SetInt("DstFactorA", (int)UnityEngine.Rendering.BlendMode.One);

            matRayMarch.SetInt("_RayMarchBlendOver", 0);

            mvPerSliceFarToNear.Reverse(); // make it nearest-to-farthest

            for (int zz = zBoundary + 1; zz < numMetavoxelsZ; zz++)
            {
                foreach (MetavoxelSortData vv in mvPerSliceFarToNear)
                {
                    //Vector3 cam2mv = Camera.main.transform.position - mvGrid[zz, vv.y, vv.x].mPos;
                    int xx = (int)vv.x, yy = (int)vv.y;

                    if (mvGrid[zz, yy, xx].mParticlesCovered.Count != 0)
                    {
                        //matRayMarchUnder.renderQueue = 5000 + mvCount;
                        //Debug.Log("N2F" + mvCount + "(" + vv.x + "," + vv.y + "," + zz + ") at dist " + cam2mv.sqrMagnitude);
                        RenderMetavoxel(xx, yy, zz, mvCount++);
                    }
                }
            }

        }


        void SetRaymarchPassConstants()
        {
            // Resources
            matRayMarch.SetTexture("_LightPropogationTexture", lightPropogationUAV);

            // Metavoxel grid uniforms
            matRayMarch.SetFloat("_NumVoxels", numVoxelsInMetavoxel);
            matRayMarch.SetVector("_MetavoxelSize", mvScale);
            matRayMarch.SetVector("_MetavoxelGridDim", new Vector3(numMetavoxelsX, numMetavoxelsY, numMetavoxelsZ));
            matRayMarch.SetVector("_MetavoxelGridCenter", wsGridCenter);
            matRayMarch.SetInt("_MetavoxelBorderSize", numBorderVoxels);

            // Camera uniforms
            //matRayMarch.SetVector("_CameraWorldPos", Camera.main.transform.position);
            // Unity sets the _CameraToWorld and _WorldToCamera constant buffers by default - but these would be on the metavoxel camera
            // that's attached to the directional light. We're interested in the main camera's matrices, not the pseudo-mv cam!
            //matRayMarch.SetMatrix("_CameraToWorldMatrix", Camera.main.cameraToWorldMatrix);
            matRayMarch.SetMatrix("_WorldToCameraMatrix", Camera.main.worldToCameraMatrix);
            matRayMarch.SetFloat("_Fov", Mathf.Deg2Rad * Camera.main.fieldOfView);
            //matRayMarch.SetFloat("_Near", Camera.main.nearClipPlane);
            //matRayMarch.SetFloat("_Far", Camera.main.farClipPlane);
            matRayMarch.SetVector("_ScreenRes", new Vector2(Screen.width, Screen.height));

            // Ray march uniforms
            matRayMarch.SetInt("_NumRaymarchStepsPerMV", rayMarchSteps);
            //m.SetVector("_AABBMin", pBounds.aabb.min);
            //m.SetVector("_AABBMax", pBounds.aabb.max);

            int showNumSamples_i = 0;
            if (bShowRayMarchSamplesPerPixel)
                showNumSamples_i = 1;

            matRayMarch.SetInt("_ShowNumSamples", showNumSamples_i);

            int showMetavoxelDrawOrder_i = 0;
            if (bShowMetavoxelDrawOrder)
                showMetavoxelDrawOrder_i = 1;

            matRayMarch.SetInt("_ShowMetavoxelDrawOrder", showMetavoxelDrawOrder_i);
            matRayMarch.SetInt("_NumMetavoxelsCovered", numMetavoxelsCovered);

            int showRayMarchBlendFunc_i = 0;
            if (bShowRayMarchBlendFunc)
                showRayMarchBlendFunc_i = 1;

            matRayMarch.SetInt("_ShowRayMarchBlendFunc", showRayMarchBlendFunc_i);            
            matRayMarch.SetInt("_SoftDistance", softParticleStepDistance);
        }


        void RenderMetavoxel(int xx, int yy, int zz, int orderIndex)
        {
            //Debug.Log("rendering mv " + xx + "," + yy +"," + zz);
            mvFillTextures[zz, yy, xx].filterMode = FilterMode.Bilinear;
            mvFillTextures[zz, yy, xx].wrapMode = TextureWrapMode.Repeat;
            mvFillTextures[zz, yy, xx].anisoLevel = volumeTextureAnisoLevel;

            matRayMarch.SetTexture("_VolumeTexture", mvFillTextures[zz, yy, xx]);
            Matrix4x4 mvToWorld = Matrix4x4.TRS(mvGrid[zz, yy, xx].mPos,
                                                lightOrientation,
                                                mvScale); // border should NOT be included here. we want to rasterize only the pixels covered by the metavoxel
            matRayMarch.SetMatrix("_MetavoxelToWorld", mvToWorld);
            matRayMarch.SetMatrix("_CameraToMetavoxel", mvToWorld.inverse * Camera.main.cameraToWorldMatrix);
            //matRayMarch.SetMatrix("_WorldToMetavoxel", mvToWorld.inverse);
            matRayMarch.SetVector("_MetavoxelIndex", new Vector3(xx, yy, zz));
            matRayMarch.SetFloat("_ParticleCoverageRatio", mvGrid[zz, yy, xx].mParticlesCovered.Count / (float)numParticlesEmitted);
            matRayMarch.SetInt("_OrderIndex", orderIndex);
            

            // Absence of the line below caused several hours of debugging madness.
            // SetPass needs to be called AFTER all material properties are set prior to every DrawMeshNow call.
            bool setPass = matRayMarch.SetPass(0);
            if (!setPass)
            {
                Debug.LogError("material set pass returned false;..");
            }

            Graphics.DrawMeshNow(cubeMesh, Vector3.zero, Quaternion.identity);
        }


        void CreateMeshes()
        {
            CreateCubeMesh();
            CreateQuadMesh();
        }


        void CreateCubeMesh()
        {
            cubeMesh = new Mesh();

            float length = 1f;
            float width = 1f;
            float height = 1f;

            #region Vertices
            Vector3 p0 = new Vector3(-length * .5f, -width * .5f, height * .5f);
            Vector3 p1 = new Vector3(length * .5f, -width * .5f, height * .5f);
            Vector3 p2 = new Vector3(length * .5f, -width * .5f, -height * .5f);
            Vector3 p3 = new Vector3(-length * .5f, -width * .5f, -height * .5f);

            Vector3 p4 = new Vector3(-length * .5f, width * .5f, height * .5f);
            Vector3 p5 = new Vector3(length * .5f, width * .5f, height * .5f);
            Vector3 p6 = new Vector3(length * .5f, width * .5f, -height * .5f);
            Vector3 p7 = new Vector3(-length * .5f, width * .5f, -height * .5f);

            Vector3[] vertices = new Vector3[]
{
    // Bottom
    p0, p1, p2, p3,
 
    // Left
    p7, p4, p0, p3,
 
    // Front
    p4, p5, p1, p0,
 
    // Back
    p6, p7, p3, p2,
 
    // Right
    p5, p6, p2, p1,
 
    // Top
    p7, p6, p5, p4
};
            #endregion

            #region Normales
            Vector3 up = Vector3.up;
            Vector3 down = Vector3.down;
            Vector3 front = Vector3.forward;
            Vector3 back = Vector3.back;
            Vector3 left = Vector3.left;
            Vector3 right = Vector3.right;

            Vector3[] normales = new Vector3[]
{
    // Bottom
    down, down, down, down,
 
    // Left
    left, left, left, left,
 
    // Front
    front, front, front, front,
 
    // Back
    back, back, back, back,
 
    // Right
    right, right, right, right,
 
    // Top
    up, up, up, up
};
            #endregion

            #region UVs
            Vector2 _00 = new Vector2(0f, 0f);
            Vector2 _10 = new Vector2(1f, 0f);
            Vector2 _01 = new Vector2(0f, 1f);
            Vector2 _11 = new Vector2(1f, 1f);

            Vector2[] uvs = new Vector2[]
{
    // Bottom
    _11, _01, _00, _10,
 
    // Left
    _11, _01, _00, _10,
 
    // Front
    _11, _01, _00, _10,
 
    // Back
    _11, _01, _00, _10,
 
    // Right
    _11, _01, _00, _10,
 
    // Top
    _11, _01, _00, _10,
};
            #endregion

            #region Triangles
            int[] triangles = new int[]
{
    // Bottom
    3, 1, 0,
    3, 2, 1,			
 
    // Left
    3 + 4 * 1, 1 + 4 * 1, 0 + 4 * 1,
    3 + 4 * 1, 2 + 4 * 1, 1 + 4 * 1,
 
    // Front
    3 + 4 * 2, 1 + 4 * 2, 0 + 4 * 2,
    3 + 4 * 2, 2 + 4 * 2, 1 + 4 * 2,
 
    // Back
    3 + 4 * 3, 1 + 4 * 3, 0 + 4 * 3,
    3 + 4 * 3, 2 + 4 * 3, 1 + 4 * 3,
 
    // Right
    3 + 4 * 4, 1 + 4 * 4, 0 + 4 * 4,
    3 + 4 * 4, 2 + 4 * 4, 1 + 4 * 4,
 
    // Top
    3 + 4 * 5, 1 + 4 * 5, 0 + 4 * 5,
    3 + 4 * 5, 2 + 4 * 5, 1 + 4 * 5,
 
};
            #endregion

            cubeMesh.vertices = vertices;
            cubeMesh.normals = normales;
            cubeMesh.uv = uvs;
            cubeMesh.triangles = triangles;

            cubeMesh.RecalculateBounds();
            cubeMesh.Optimize();
        }


        void CreateQuadMesh()
        {
            quadMesh = new Mesh();

            Vector3 botleft = new Vector3(-1f, -1f, 0f),
                    botRight = new Vector3(1f, -1f, 0f),
                    topRight = new Vector3(1f, 1f, 0f),
                    topLeft = new Vector3(-1f, 1f, 0f);

            quadMesh.vertices = new Vector3[] {
                botleft, botRight, topRight, topLeft                
            };

            quadMesh.triangles = new int[] {
                                            0, 1, 2,
                                            0, 2, 3
                                        };
        }


        public void DrawMetavoxelGrid()
        {
            for (int zz = 0; zz < numMetavoxelsZ; zz++)
            {
                for (int yy = 0; yy < numMetavoxelsY; yy++)
                {
                    for (int xx = 0; xx < numMetavoxelsX; xx++)
                    {
                        if (mvGrid[zz, yy, xx].mParticlesCovered.Count != 0)
                            DrawMetavoxel(xx, yy, zz);
                    }
                }
            }
        }


        void DrawMetavoxel(int xx, int yy, int zz)
        {
            List<Vector3> points = new List<Vector3>();
            mvLineColor.SetPass(0);
            Vector3 mvPos = mvGrid[zz, yy, xx].mPos;
            Quaternion q = lightOrientation;


            float halfWidth = mvScale.x * 0.5f, halfHeight = mvScale.y * 0.5f, halfDepth = mvScale.z * 0.5f;
            // back, front --> Z ; top, bot --> Y ; left, right --> X
            Vector3 offBotLeftBack = q * new Vector3(-halfWidth, -halfHeight, halfDepth),
                    offBotLeftFront = q * new Vector3(-halfWidth, -halfHeight, -halfDepth),
                    offTopLeftBack = q * new Vector3(-halfWidth, halfHeight, halfDepth),
                    offTopLeftFront = q * new Vector3(-halfWidth, halfHeight, -halfDepth),
                    offBotRightBack = q * new Vector3(halfWidth, -halfHeight, halfDepth),
                    offBotRightFront = q * new Vector3(halfWidth, -halfHeight, -halfDepth),
                    offTopRightBack = q * new Vector3(halfWidth, halfHeight, halfDepth),
                    offTopRightFront = q * new Vector3(halfWidth, halfHeight, -halfDepth);

            // left 
            points.Add(mvPos + offBotLeftBack);
            points.Add(mvPos + offBotLeftFront);
            points.Add(mvPos + offBotLeftFront);
            points.Add(mvPos + offTopLeftFront);
            points.Add(mvPos + offTopLeftFront);
            points.Add(mvPos + offTopLeftBack);
            points.Add(mvPos + offTopLeftBack);
            points.Add(mvPos + offBotLeftBack);
            // right
            points.Add(mvPos + offBotRightBack);
            points.Add(mvPos + offBotRightFront);
            points.Add(mvPos + offBotRightFront);
            points.Add(mvPos + offTopRightFront);
            points.Add(mvPos + offTopRightFront);
            points.Add(mvPos + offTopRightBack);
            points.Add(mvPos + offTopRightBack);
            points.Add(mvPos + offBotRightBack);
            // join left and right
            points.Add(mvPos + offTopLeftBack);
            points.Add(mvPos + offTopRightBack);
            points.Add(mvPos + offTopLeftFront);
            points.Add(mvPos + offTopRightFront);

            points.Add(mvPos + offBotLeftBack);
            points.Add(mvPos + offBotRightBack);
            points.Add(mvPos + offBotLeftFront);
            points.Add(mvPos + offBotRightFront);

            GL.Begin(GL.LINES);
            foreach (Vector3 v in points)
            {
                GL.Vertex3(v.x, v.y, v.z);
            }
            GL.End();
        }


        /*********************** Gui callback setters **************************************
         *  Functions below have been hooked to the appropriate GUI element in the inspector
         */        
        //-- render options
        public void SetDisplacementScale(float ds)
        {
            fDisplacementScale = ds;
        }


        public void SetRayMarchSteps(float steps)
        {
            rayMarchSteps = (int) steps;
        }


        public void SetGridDimensions(float a)
        {
            // Resize grid
            //ResizeMVGrid(a);
        }

        public void SetGridScale(float s)
        {
            mvScale = new Vector3(s, s, s);
            UpdateMetavoxelPositions();
        }

        //-- particle options
        public void SetFadeOutParticles(bool fade)
        {
            fadeOutParticles = fade;
        }

        public void SetParticleOpacityFactor(float f)
        {
            opacityFactor = f;
        }

        public void SetNumParticles(float n)
        {
            particleSys.maxParticles = (int)n;
        }

        public void SetFadeParticles(bool fade)
        {
            fadeOutParticles = fade;
        }

        //-- render debug options
        public void SetShowMetavoxelGrid(bool show)
        {
            bShowMetavoxelGrid = show;
        }

        public void SetShowRayMarchSamples(bool show)
        {
            bShowRayMarchSamplesPerPixel = show;
        }

        public void SetShowMetavoxelDrawOrder(bool show)     
        {
            bShowMetavoxelDrawOrder = show;
        }

        public void SetShowRayMarchBlendFunc(bool show)
        {
            bShowRayMarchBlendFunc = show;
        }

        public void SetUpdateInterval(float interval)
        {
            updateInterval = (int) interval;
        }

        public void SetTimeScale(float ts)
        {
            Time.timeScale = ts;
        }

        public void SetSoftParticleDistance(float stepDistance)
        {
            softParticleStepDistance = (int) stepDistance;
        }

        #if UNITY_EDITOR
        void OnDrawGizmos()
        {
            Gizmos.color = Color.blue;
            Vector3 lsWorldOrigin = dirLight.transform.worldToLocalMatrix.MultiplyPoint3x4(wsGridCenter); // xform origin to light space

            for (int zz = 0; zz < numMetavoxelsZ; zz++)
            {
                for (int yy = 0; yy < numMetavoxelsY; yy++)
                {
                    for (int xx = 0; xx < numMetavoxelsX; xx++)
                    {                        
                        // if the scene isn't playing, the metavoxel grid wouldn't have been created.
                        // recalculate metavoxel positions and rotations based on the light
                        Vector3 lsOffset = Vector3.Scale(new Vector3(numMetavoxelsX / 2 - xx, numMetavoxelsY / 2 - yy, numMetavoxelsZ / 2 - zz), mvScale);
                        Vector3 wsMetavoxelPos = dirLight.transform.localToWorldMatrix.MultiplyPoint3x4(lsWorldOrigin - lsOffset); // using - lsOffset means that zz = 0 is closer to the light
                        Quaternion q = new Quaternion();
                        q.SetLookRotation(dirLight.transform.forward, dirLight.transform.up);
                        Gizmos.matrix = Matrix4x4.TRS(wsMetavoxelPos, q, mvScale);
                        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
                    }
                }
            }
        }
        #endif        
    }



}