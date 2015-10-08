﻿//This class was generated by ME3Explorer
//Author: Warranty Voider
//URL: http://sourceforge.net/projects/me3explorer/
//URL: http://me3explorer.freeforums.org/
//URL: http://www.facebook.com/pages/Creating-new-end-for-Mass-Effect-3/145902408865659
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ME3Explorer.Unreal;
using Microsoft.DirectX;
using Microsoft.DirectX.Direct3D;
using lib3ds.Net;
using MEGeneral.Debugging;

namespace ME3Explorer.Unreal.Classes
{
    public class StaticMeshActor
    {
        #region Unreal Props

        //Bool Properties

        public bool bCollideActors = false;
        public bool bCanStepUpOn = false;
        public bool bPathColliding = false;
        public bool bLockLocation = false;
        public bool OverridePhysMat = false;
        public bool bHidden = false;
        public bool bShadowParented = false;
        public bool bCollideComplex = false;
        public bool bHiddenEd = false;
        //Name Properties

        public int Tag;
        public int Group;
        public int UniqueTag;
        //Object Properties

        public int StaticMeshComponent;
        public int CollisionComponent;
        //Float Properties

        public float DrawScale = 1.0f;
        public float CreationTime;
        public float AudioOcclusion;
        //Vector3 Properties

        public Vector3 DrawScale3D = new Vector3(1, 1, 1);
        public Vector3 Rotator;
        public Vector3 location;

        #endregion

        public int MyIndex;
        public PCCObject pcc;
        public byte[] data;
        public List<PropertyReader.Property> Props;
        public StaticMeshComponent STMC;
        public Matrix MyMatrix;
        public bool isEdited = false;

        public StaticMeshActor(PCCObject Pcc, int Index)
        {
            pcc = Pcc;
            MyIndex = Index;
            if (pcc.isExport(Index))
                data = pcc.Exports[Index].Data;
            Props = PropertyReader.getPropList(pcc, data);
            BitConverter.IsLittleEndian = true;
            foreach (PropertyReader.Property p in Props)
            {
                string s =pcc.getNameEntry(p.Name);
                switch (s)
                {
                    #region
                    case "bCollideActors":
                        if (p.raw[p.raw.Length - 1] == 1)
                            bCollideActors = true;
                        break;
                    case "bCanStepUpOn":
                        if (p.raw[p.raw.Length - 1] == 1)
                            bCanStepUpOn = true;
                        break;
                    case "bPathColliding":
                        if (p.raw[p.raw.Length - 1] == 1)
                            bPathColliding = true;
                        break;
                    case "bLockLocation":
                        if (p.raw[p.raw.Length - 1] == 1)
                            bLockLocation = true;
                        break;
                    case "OverridePhysMat":
                        if (p.raw[p.raw.Length - 1] == 1)
                            OverridePhysMat = true;
                        break;
                    case "bHidden":
                        if (p.raw[p.raw.Length - 1] == 1)
                            bHidden = true;
                        break;
                    case "bShadowParented":
                        if (p.raw[p.raw.Length - 1] == 1)
                            bShadowParented = true;
                        break;
                    case "bCollideComplex":
                        if (p.raw[p.raw.Length - 1] == 1)
                            bCollideComplex = true;
                        break;
                    case "bHiddenEd":
                        if (p.raw[p.raw.Length - 1] == 1)
                            bHiddenEd = true;
                        break;
                    case "Tag":
                        Tag = p.Value.IntValue;
                        break;
                    case "Group":
                        Group = p.Value.IntValue;
                        break;
                    case "UniqueTag":
                        UniqueTag = p.Value.IntValue;
                        break;
                    case "StaticMeshComponent":
                        StaticMeshComponent = p.Value.IntValue;
                        if (pcc.isExport(StaticMeshComponent - 1) && pcc.Exports[StaticMeshComponent - 1].ClassName == "StaticMeshComponent")
                            STMC = new StaticMeshComponent(pcc, StaticMeshComponent - 1);
                        break;
                    case "CollisionComponent":
                        CollisionComponent = p.Value.IntValue;
                        break;
                    case "DrawScale":
                        DrawScale = BitConverter.ToSingle(p.raw, p.raw.Length - 4);
                        break;
                    case "CreationTime":
                        CreationTime = BitConverter.ToSingle(p.raw, p.raw.Length - 4);
                        break;
                    case "AudioOcclusion":
                        AudioOcclusion = BitConverter.ToSingle(p.raw, p.raw.Length - 4);
                        break;
                    #endregion
                    case "DrawScale3D":
                        DrawScale3D = new Vector3(BitConverter.ToSingle(p.raw, p.raw.Length - 12),
                                              BitConverter.ToSingle(p.raw, p.raw.Length - 8),
                                              BitConverter.ToSingle(p.raw, p.raw.Length - 4));
                        break;
                    case "Rotation":
                        Rotator = new Vector3(BitConverter.ToInt32(p.raw, p.raw.Length - 12),
                                              BitConverter.ToInt32(p.raw, p.raw.Length - 8),
                                              BitConverter.ToInt32(p.raw, p.raw.Length - 4));
                        break;
                    case "location":
                        location = new Vector3(BitConverter.ToSingle(p.raw, p.raw.Length - 12),
                                              BitConverter.ToSingle(p.raw, p.raw.Length - 8),
                                              BitConverter.ToSingle(p.raw, p.raw.Length - 4));
                        break;
                }
            }
            MyMatrix = Matrix.Identity;            
            MyMatrix *= Matrix.Scaling(DrawScale3D);
            MyMatrix *= Matrix.Scaling(new Vector3(DrawScale, DrawScale, DrawScale));
            Vector3 rot = RotatorToDX(Rotator);
            MyMatrix *= Matrix.RotationYawPitchRoll(rot.X, rot.Y, rot.Z);
            MyMatrix *= Matrix.Translation(location);
        }

        public Vector3 RotatorToDX(Vector3 v)
        {
            Vector3 r = v;
            r.X = (int)r.X % 65536;
            r.Y = (int)r.Y % 65536;
            r.Z = (int)r.Z % 65536;
            float f = (3.1415f * 2f) / 65536f;
            r.X = v.Z * f;
            r.Y = v.X * f;
            r.Z = v.Y * f;
            return r;
        }

        public Vector3 DxToRotator(Vector3 v)
        {
            Vector3 r = new Vector3();
            float f = 65536f / (3.1415f * 2f);
            r.X = -v.X * f; 
            r.Y = v.Z * f;            
            r.Z = -v.Y * f;
            r.X = (int)r.X % 65536;
            r.Y = (int)r.Y % 65536;
            r.Z = (int)r.Z % 65536;
            return r;
        }

        public void Render(Device device)
        {
            if (STMC != null)
                STMC.Render(device, MyMatrix);
        }

        public void ProcessTreeClick(int[] path, bool AutoFocus)
        {
            if (STMC != null)
                STMC.SetSelection(true);
        }

        public float Process3DClick(Vector3 org, Vector3 dir)
        {
            float dist = -1f;
            if (STMC != null)
            {
                float d = STMC.Process3DClick(org, dir, MyMatrix);
                if ((d < dist && d > 0) || (dist == -1 && d > 0))
                    dist = d;
            }
            return dist;
        }

        public void ApplyTransform(Matrix m)
        {
                if (STMC != null && STMC.GetSelection() == true)
                {
                    isEdited = true;
                    MyMatrix *= m;
                }
        }

        public void ApplyRotation(Vector3 v)
        {
                if (STMC != null && STMC.GetSelection() == true)
                {
                    isEdited = true;
                    Matrix m = MyMatrix;
                    Vector3 v2 = new Vector3(m.M41, m.M42, m.M43);
                    float f = 3.1415f / 180f;
                    m.M41 = 0;
                    m.M42 = 0;
                    m.M43 = 0;
                    m *= Matrix.RotationYawPitchRoll(-v.X * f, -v.Y * f, v.Z * f);
                    m.M41 = v2.X;
                    m.M42 = v2.Y;
                    m.M43 = v2.Z;
                    MyMatrix = m;
                }
        }

        public void SaveChanges()
        {
            if (isEdited)
            {
                Matrix m = MyMatrix;
                Vector3 loc = new Vector3(m.M41, m.M42, m.M43);
                byte[] buff = Vector3ToBuff(loc);
                int f = -1;
                for (int i = 0; i < Props.Count; i++)
                    if (pcc.getNameEntry(Props[i].Name) == "location")
                    { 
                        f = i; 
                        break; 
                    };
                if (f != -1)//has prop
                {
                    int off = Props[f].offend - 12;
                    for (int i = 0; i < 12; i++)
                        data[off + i] = buff[i];
                }
                else//have to add prop
                {
                    DebugOutput.PrintLn(MyIndex + " : cant find location property");
                }                
                Vector3 rot = new Vector3((float)Math.Atan2(m.M32, m.M33), (float)Math.Asin(-1 * m.M31), (float)Math.Atan2(-1 * m.M21, m.M11));
                rot = DxToRotator(rot);
                buff = RotatorToBuff(rot);
                f = -1;
                for (int i = 0; i < Props.Count; i++)
                    if (pcc.getNameEntry(Props[i].Name) == "Rotation")
                    {
                        f = i;
                        break;
                    };
                if (f != -1)//has prop
                {
                    int off = Props[f].offend - 12;
                    for (int i = 0; i < 12; i++)
                        data[off + i] = buff[i];
                }
                else//have to add prop
                {
                    DebugOutput.PrintLn(MyIndex + " : cant find rotation property");
                }
                pcc.Exports[MyIndex].Data = data;
            }
        }

        public void CreateModJobs()
        {
            if (isEdited)
            {
                Matrix m = MyMatrix;
                Vector3 loc = new Vector3(m.M41, m.M42, m.M43);
                byte[] buff = Vector3ToBuff(loc);
                int f = -1;
                for (int i = 0; i < Props.Count; i++)
                    if (pcc.getNameEntry(Props[i].Name) == "location")
                    {
                        f = i;
                        break;
                    };
                if (f != -1)//has prop
                {
                    int off = Props[f].offend - 12;
                    for (int i = 0; i < 12; i++)
                        data[off + i] = buff[i];
                }
                else//have to add prop
                {
                    DebugOutput.PrintLn(MyIndex + " : cant find location property");
                }
                Vector3 rot = new Vector3((float)Math.Atan2(m.M32, m.M33), (float)Math.Asin(-1 * m.M31), (float)Math.Atan2(-1 * m.M21, m.M11));
                rot = DxToRotator(rot);
                buff = RotatorToBuff(rot);
                f = -1;
                for (int i = 0; i < Props.Count; i++)
                    if (pcc.getNameEntry(Props[i].Name) == "Rotation")
                    {
                        f = i;
                        break;
                    };
                if (f != -1)//has prop
                {
                    int off = Props[f].offend - 12;
                    for (int i = 0; i < 12; i++)
                        data[off + i] = buff[i];
                }
                else//have to add prop
                {
                    DebugOutput.PrintLn(MyIndex + " : cant find rotation property");
                }
                KFreonLib.Scripting.ModMaker.ModJob mj = new KFreonLib.Scripting.ModMaker.ModJob();
                string currfile = Path.GetFileName(pcc.pccFileName);
                mj.data = data;
                mj.Name = "Binary Replacement for file \"" + currfile + "\" in Object #" + MyIndex + " with " + data.Length + " bytes of data";
                string lc = Path.GetDirectoryName(Application.ExecutablePath);
                string template = System.IO.File.ReadAllText(lc + "\\exec\\JobTemplate_Binary2.txt");
                template = template.Replace("**m1**", MyIndex.ToString());
                template = template.Replace("**m2**", currfile);
                mj.Script = template;
                KFreonLib.Scripting.ModMaker.JobList.Add(mj);
                DebugOutput.PrintLn("Created Mod job : " + mj.Name);
            }
        }

        public byte[] Vector3ToBuff(Vector3 v)
        {
            MemoryStream m = new MemoryStream();
            BitConverter.IsLittleEndian = true;
            m.Write(BitConverter.GetBytes(v.X), 0, 4);
            m.Write(BitConverter.GetBytes(v.Y), 0, 4);
            m.Write(BitConverter.GetBytes(v.Z), 0, 4);
            return m.ToArray();
        }

        public byte[] RotatorToBuff(Vector3 v)
        {
            MemoryStream m = new MemoryStream();
            BitConverter.IsLittleEndian = true;
            m.Write(BitConverter.GetBytes((int)v.X), 0, 4);
            m.Write(BitConverter.GetBytes((int)v.Y), 0, 4);
            m.Write(BitConverter.GetBytes((int)v.Z), 0, 4);
            return m.ToArray();
        }

        public void SetSelection(bool Selected)
        {
            if (STMC != null)
                STMC.SetSelection(Selected);
        }

        public void Export3DS(Lib3dsFile f)
        {
           if (STMC != null)
                {
                    DebugOutput.PrintLn("Exported " + pcc.Exports[MyIndex].ObjectName + " #" + MyIndex, false);
                    STMC.Export3DS(f, MyMatrix);
                }
            DebugOutput.PrintLn("STMA done.");
        }

        public TreeNode ToTree()
        {
            TreeNode res = new TreeNode(pcc.Exports[MyIndex].ObjectName + "(#" + MyIndex + ")");
            res.Nodes.Add("bCollideActors : " + bCollideActors);
            res.Nodes.Add("bCanStepUpOn : " + bCanStepUpOn);
            res.Nodes.Add("bPathColliding : " + bPathColliding);
            res.Nodes.Add("bLockLocation : " + bLockLocation);
            res.Nodes.Add("OverridePhysMat : " + OverridePhysMat);
            res.Nodes.Add("bHidden : " + bHidden);
            res.Nodes.Add("bShadowParented : " + bShadowParented);
            res.Nodes.Add("bCollideComplex : " + bCollideComplex);
            res.Nodes.Add("bHiddenEd : " + bHiddenEd);
            res.Nodes.Add("Tag : " + pcc.getNameEntry(Tag));
            res.Nodes.Add("Group : " + pcc.getNameEntry(Group));
            res.Nodes.Add("UniqueTag : " + pcc.getNameEntry(UniqueTag));
            res.Nodes.Add("StaticMeshComponent : " + StaticMeshComponent);
            res.Nodes.Add("CollisionComponent : " + CollisionComponent);
            res.Nodes.Add("DrawScale : " + DrawScale);
            res.Nodes.Add("CreationTime : " + CreationTime);
            res.Nodes.Add("AudioOcclusion : " + AudioOcclusion);
            if (STMC != null)
                res.Nodes.Add(STMC.ToTree());
            return res;
        }

    }
}