using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Genera una mano derecha low poly con huesos (antebrazo, mano, 5 dedos x 3 falanges)
/// y las dos animaciones grabadas en vídeo:
///   GolpeAbajo  (WhatsApp Video 23.27.09): mano en garra, puño, golpe hacia abajo, vuelve abierta y queda en garra.
///   CerrarAbrir (WhatsApp Video 23.27.47): palma arriba relajada, puño rápido, se abre de golpe con los dedos separados.
/// Pensada para primera persona: el prefab va como hijo de la cámara en (0,0,0).
/// Menú: Tools > Manos Low Poly.
/// </summary>
public static class ManoLowPolyGenerator
{
    const string Dir = "Assets/Manos";
    const float FPS = 30f;

    // ---------- Proporciones (metros) ----------
    const float ForearmLen = 0.24f;
    const float PalmLen = 0.09f;
    static readonly string[] FingerNames = { "Indice", "Corazon", "Anular", "Menique" };
    // El pulgar está en -X, la palma mira a +Z, los dedos apuntan a +Y.
    static readonly Vector3[] FingerBase =
    {
        new Vector3(-0.030f, 0.088f, 0f), new Vector3(-0.010f, 0.092f, 0f),
        new Vector3(0.010f, 0.088f, 0f), new Vector3(0.029f, 0.080f, 0f),
    };
    static readonly float[][] FingerLen =
    {
        new[] { 0.040f, 0.025f, 0.020f }, new[] { 0.045f, 0.028f, 0.022f },
        new[] { 0.042f, 0.026f, 0.020f }, new[] { 0.032f, 0.020f, 0.018f },
    };
    static readonly float[] FingerWidth = { 0.0175f, 0.0185f, 0.0175f, 0.0155f };
    static readonly float[] RestSplay = { 4f, 0f, -3f, -7f };     // grados sobre Z (+ = hacia el pulgar)
    static readonly float[] SpreadDir = { 1f, 0.2f, -0.6f, -1.2f };
    static readonly float[] ThumbLen = { 0.040f, 0.032f, 0.025f };
    static readonly Vector3 ThumbBase = new Vector3(-0.032f, 0.018f, 0.010f);

    // ---------- Pose ----------
    class Pose
    {
        public Vector3 armPos, armEuler;
        public float armTwist;
        public Vector3 wrist;
        public float[] baseCurl = new float[4], midCurl = new float[4];
        public float spread, thumbTilt, thumbFold, thumbCurl, wiggle, shake;

        public Pose Clone()
        {
            var p = (Pose)MemberwiseClone();
            p.baseCurl = (float[])baseCurl.Clone();
            p.midCurl = (float[])midCurl.Clone();
            return p;
        }

        public Pose Arm(Vector3 pos, Vector3 euler, float twist)
        {
            var p = Clone(); p.armPos = pos; p.armEuler = euler; p.armTwist = twist; return p;
        }

        public Pose Wrist(Vector3 w) { var p = Clone(); p.wrist = w; return p; }

        public static Pose Lerp(Pose a, Pose b, float t)
        {
            var p = new Pose
            {
                armPos = Vector3.Lerp(a.armPos, b.armPos, t),
                armEuler = Vector3.Lerp(a.armEuler, b.armEuler, t),
                armTwist = Mathf.Lerp(a.armTwist, b.armTwist, t),
                wrist = Vector3.Lerp(a.wrist, b.wrist, t),
                spread = Mathf.Lerp(a.spread, b.spread, t),
                thumbTilt = Mathf.Lerp(a.thumbTilt, b.thumbTilt, t),
                thumbFold = Mathf.Lerp(a.thumbFold, b.thumbFold, t),
                thumbCurl = Mathf.Lerp(a.thumbCurl, b.thumbCurl, t),
                wiggle = Mathf.Lerp(a.wiggle, b.wiggle, t),
                shake = Mathf.Lerp(a.shake, b.shake, t),
            };
            for (int i = 0; i < 4; i++)
            {
                p.baseCurl[i] = Mathf.Lerp(a.baseCurl[i], b.baseCurl[i], t);
                p.midCurl[i] = Mathf.Lerp(a.midCurl[i], b.midCurl[i], t);
            }
            return p;
        }
    }

    static Pose Hand(float[] b, float[] m, float spread, float tTilt, float tFold, float tCurl, Vector3 wrist, float wiggle = 0f, float shake = 0f)
        => new Pose { baseCurl = b, midCurl = m, spread = spread, thumbTilt = tTilt, thumbFold = tFold, thumbCurl = tCurl, wrist = wrist, wiggle = wiggle, shake = shake };

    // Formas de mano (sacadas de los fotogramas)
    static Pose Garra() => Hand(new[] { 18f, 20f, 24f, 28f }, new[] { 70f, 78f, 80f, 75f }, 6f, 42f, 18f, 22f, new Vector3(-22f, 0, 0), wiggle: 1f);
    static Pose Puno() => Hand(new[] { 88f, 90f, 92f, 92f }, new[] { 100f, 105f, 105f, 100f }, -4f, 6f, 26f, 32f, new Vector3(-5f, 0, 0));
    static Pose Abierta() => Hand(new[] { -4f, -2f, 0f, 2f }, new[] { 12f, 12f, 14f, 14f }, 9f, 50f, 4f, 14f, new Vector3(-12f, 0, 0));
    static Pose Cuenco() => Hand(new[] { 32f, 36f, 40f, 44f }, new[] { 38f, 42f, 46f, 50f }, 4f, 40f, 14f, 24f, new Vector3(-35f, 0, 0), wiggle: 0.35f);
    static Pose AbiertaAncha(float extra) => Hand(new[] { -10f - extra, -8f - extra, -6f - extra, -4f - extra }, new[] { 2f, 2f, 3f, 4f }, 14f + extra, 58f + extra, -4f, 12f, new Vector3(-16f - extra, 0, 0));

    struct Key { public float t; public Pose p; public Key(float t, Pose p) { this.t = t; this.p = p; } }

    // ---------- Animación 1: golpe hacia abajo ----------
    static List<Key> KeysGolpe()
    {
        var arriba = new Vector3(0.16f, -0.30f, 0.30f); var arribaE = new Vector3(10f, 0f, 20f); const float tw = -60f;
        var garra = Garra().Arm(arriba, arribaE, tw);
        var puno = Puno().Arm(arriba, arribaE, tw);
        puno.shake = 1f;
        var carga = Puno().Arm(new Vector3(0.17f, -0.26f, 0.28f), new Vector3(-2f, 0f, 24f), tw - 5f);
        var golpe = Puno().Arm(new Vector3(0.22f, -0.85f, 0.40f), new Vector3(100f, 0f, 8f), -85f);
        var abajoAbierta = Abierta().Arm(new Vector3(0.22f, -0.85f, 0.38f), new Vector3(70f, 0f, 10f), -70f);
        var vuelve = Abierta().Arm(new Vector3(0.15f, -0.27f, 0.33f), new Vector3(18f, 0f, 16f), -55f).Wrist(new Vector3(-28f, 0, 0));
        vuelve.spread = 12f;

        return new List<Key>
        {
            new Key(0.00f, garra),
            new Key(2.30f, garra),
            new Key(2.45f, puno),          // cierra rápido
            new Key(2.95f, puno),          // aguanta el puño
            new Key(3.10f, carga),         // pequeña carga hacia atrás
            new Key(3.30f, golpe),         // golpe abajo, sale de plano
            new Key(4.10f, golpe),
            new Key(4.30f, abajoAbierta),  // se abre fuera de plano
            new Key(4.62f, vuelve),        // sube abierta con algo de inercia
            new Key(5.15f, garra),         // se recoge en garra
            new Key(6.40f, garra),
        };
    }

    // ---------- Animación 2: cerrar y abrir ----------
    static List<Key> KeysAbrir()
    {
        var pos = new Vector3(0.14f, -0.24f, 0.08f); var eul = new Vector3(65f, 0f, 15f); const float tw = 180f;
        var cuenco = Cuenco().Arm(pos, eul, tw);
        var puno = Puno().Arm(pos + new Vector3(0f, 0.02f, 0.01f), new Vector3(60f, 0f, 15f), tw).Wrist(new Vector3(-12f, 0, 0));
        puno.shake = 1f;
        var estallido = AbiertaAncha(6f).Arm(pos + new Vector3(0f, 0.005f, 0.01f), new Vector3(63f, 0f, 15f), tw);
        var abierta = AbiertaAncha(0f).Arm(pos, new Vector3(64f, 0f, 15f), tw);

        return new List<Key>
        {
            new Key(0.00f, cuenco),
            new Key(2.15f, cuenco),
            new Key(2.30f, puno),       // puño rápido
            new Key(2.85f, puno),       // aprieta
            new Key(3.05f, estallido),  // se abre de golpe
            new Key(3.35f, abierta),
            new Key(4.20f, abierta),
        };
    }

    // ---------- Manos del jugador (solo dedos y muñeca: el brazo se queda donde lo coloques) ----------
    const float IdleLen = 4f; // las frecuencias del meneo de dedos dan ciclos enteros en 4 s -> bucle sin salto

    static List<Key> KeysIdle()
    {
        var g = Garra();
        return new List<Key> { new Key(0f, g), new Key(IdleLen, g) };
    }

    // Derecha, al disparar: puño rápido, se abre del todo y vuelve a la garra.
    static List<Key> KeysDisparo()
    {
        var g = Garra(); var p = Puno(); var a = Abierta(); a.wrist = new Vector3(-26f, 0, 0); a.spread = 12f;
        return new List<Key>
        {
            new Key(0.00f, g),
            new Key(0.06f, p),
            new Key(0.18f, p),
            new Key(0.34f, a),
            new Key(0.75f, g),
        };
    }

    // Izquierda, tecla E: puño, aguanta mientras va contra la cámara (el movimiento lo hace ManoJugador) y se abre.
    static List<Key> KeysPunoCamara()
    {
        var g = Garra(); var p = Puno(); p.shake = 0f;
        return new List<Key>
        {
            new Key(0.00f, g),
            new Key(0.10f, p),
            new Key(0.65f, p),
            new Key(1.00f, g),
        };
    }

    static float Smooth(float u) => u * u * (3f - 2f * u);

    static Pose Sample(List<Key> keys, float t)
    {
        if (t <= keys[0].t) return keys[0].p;
        for (int i = 1; i < keys.Count; i++)
            if (t <= keys[i].t)
            {
                float u = Mathf.InverseLerp(keys[i - 1].t, keys[i].t, t);
                return Pose.Lerp(keys[i - 1].p, keys[i].p, Smooth(u));
            }
        return keys[keys.Count - 1].p;
    }

    // ---------- Rig ----------
    class Rig
    {
        public Transform root, forearm, hand;
        public Transform[][] fingers = new Transform[4][];
        public Transform[] thumb = new Transform[3];
        public List<Transform> all = new List<Transform>();
    }

    static Transform Bone(string name, Transform parent, Vector3 localPos, Rig rig)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = localPos;
        rig.all.Add(t);
        return t;
    }

    static Rig BuildRig()
    {
        var rig = new Rig();
        rig.root = new GameObject("ManoLowPoly").transform;
        rig.forearm = Bone("Antebrazo", rig.root, Vector3.zero, rig);
        rig.hand = Bone("Mano", rig.forearm, new Vector3(0, ForearmLen, 0), rig);
        for (int f = 0; f < 4; f++)
        {
            rig.fingers[f] = new Transform[3];
            var parent = rig.hand; var pos = FingerBase[f];
            for (int j = 0; j < 3; j++)
            {
                rig.fingers[f][j] = Bone(FingerNames[f] + (j + 1), parent, pos, rig);
                parent = rig.fingers[f][j]; pos = new Vector3(0, FingerLen[f][j], 0);
            }
        }
        {
            var parent = rig.hand; var pos = ThumbBase;
            for (int j = 0; j < 3; j++)
            {
                rig.thumb[j] = Bone("Pulgar" + (j + 1), parent, pos, rig);
                parent = rig.thumb[j]; pos = new Vector3(0, ThumbLen[j], 0);
            }
        }
        return rig;
    }

    static readonly float[] WigglePhase = { 0f, 1.7f, 3.1f, 4.4f };
    static readonly float[] WiggleFreq = { 2f, 1.5f, 2.5f, 1.5f }; // x PI -> periodo 2/f, ciclos enteros en 4 s (bucle del idle)
    static readonly float[] WiggleFreqVideo = { 2.1f, 1.7f, 2.4f, 1.9f }; // las originales de los clips de los vídeos
    static bool wiggleVideo; // true al hornear GolpeAbajo / CerrarAbrir

    // Pose -> rotaciones/posiciones locales de cada hueso
    static void Apply(Rig r, Pose p, float t)
    {
        float sh = p.shake * 0.0018f;
        r.forearm.localPosition = p.armPos + new Vector3(Mathf.Sin(t * 41f), Mathf.Sin(t * 37f + 1f), Mathf.Sin(t * 29f + 2f)) * sh;
        r.forearm.localRotation = Quaternion.Euler(p.armEuler) * Quaternion.AngleAxis(p.armTwist, Vector3.up);
        r.hand.localRotation = Quaternion.Euler(p.wrist + (wiggleVideo ? new Vector3(Mathf.Sin(t * 1.3f), 0, Mathf.Sin(t * 0.9f)) : new Vector3(Mathf.Sin(t * Mathf.PI * 0.5f), 0, Mathf.Sin(t * Mathf.PI * 0.5f + 1f))) * 2f * p.wiggle);

        for (int f = 0; f < 4; f++)
        {
            float w = p.wiggle * Mathf.Sin(t * (wiggleVideo ? WiggleFreqVideo[f] : WiggleFreq[f]) * Mathf.PI + WigglePhase[f]);
            float splay = RestSplay[f] + SpreadDir[f] * p.spread;
            float b = p.baseCurl[f] + w * 5f, m = p.midCurl[f] + w * 7f;
            r.fingers[f][0].localRotation = Quaternion.AngleAxis(splay, Vector3.forward) * Quaternion.AngleAxis(b, Vector3.right);
            r.fingers[f][1].localRotation = Quaternion.AngleAxis(m, Vector3.right);
            r.fingers[f][2].localRotation = Quaternion.AngleAxis(m * 0.75f, Vector3.right);
        }
        float tw = p.wiggle * Mathf.Sin(t * (wiggleVideo ? 1.5f : Mathf.PI) + 0.5f) * 4f;
        r.thumb[0].localRotation = Quaternion.AngleAxis(p.thumbTilt, Vector3.forward) * Quaternion.AngleAxis(40f, Vector3.up) * Quaternion.AngleAxis(p.thumbFold + tw, Vector3.right);
        r.thumb[1].localRotation = Quaternion.AngleAxis(p.thumbCurl, Vector3.right);
        r.thumb[2].localRotation = Quaternion.AngleAxis(p.thumbCurl * 1.1f, Vector3.right);
    }

    // ---------- Malla low poly (cajas facetadas, 1 hueso por caja) ----------
    class MeshBuilder
    {
        public List<Vector3> v = new List<Vector3>();
        public List<BoneWeight> w = new List<BoneWeight>();
        public List<int>[] tris = { new List<int>(), new List<int>() };

        // Caja que se estrecha a lo largo de +Y del hueso (y0 -> y1). hw/hd = medio ancho (X) / medio grosor (Z).
        public void Box(int bone, Matrix4x4 boneToMesh, float y0, float y1, float hw0, float hd0, float hw1, float hd1, float cx = 0f, float cz = 0f, int sub = 0, float bevel = 0.25f)
        {
            // Sección octogonal (esquinas recortadas) para que no parezca un bloque perfecto.
            Vector3[] Ring(float y, float hw, float hd)
            {
                float bw = hw * bevel, bd = hd * bevel;
                return new[]
                {
                    new Vector3(cx - hw + bw, y, cz - hd), new Vector3(cx + hw - bw, y, cz - hd),
                    new Vector3(cx + hw, y, cz - hd + bd), new Vector3(cx + hw, y, cz + hd - bd),
                    new Vector3(cx + hw - bw, y, cz + hd), new Vector3(cx - hw + bw, y, cz + hd),
                    new Vector3(cx - hw, y, cz + hd - bd), new Vector3(cx - hw, y, cz - hd + bd),
                };
            }
            var a = Ring(y0, hw0, hd0);
            var b = Ring(y1, hw1, hd1);
            var center = new Vector3(cx, (y0 + y1) * 0.5f, cz);
            for (int i = 0; i < 8; i++)
            {
                int n = (i + 1) % 8;
                Quad(bone, boneToMesh, center, a[i], a[n], b[n], b[i], sub);
            }
            Cap(bone, boneToMesh, center, a, sub);
            Cap(bone, boneToMesh, center, b, sub);
        }

        void Cap(int bone, Matrix4x4 m, Vector3 center, Vector3[] ring, int sub)
        {
            var c = Vector3.zero; foreach (var p in ring) c += p; c /= ring.Length;
            for (int i = 0; i < ring.Length; i++) Tri(bone, m, center, c, ring[i], ring[(i + 1) % ring.Length], sub);
        }

        void Quad(int bone, Matrix4x4 m, Vector3 center, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, int sub)
        {
            Tri(bone, m, center, p0, p1, p2, sub);
            Tri(bone, m, center, p0, p2, p3, sub);
        }

        // Cada triángulo con sus propios vértices -> sombreado plano (look low poly).
        void Tri(int bone, Matrix4x4 m, Vector3 center, Vector3 p0, Vector3 p1, Vector3 p2, int sub)
        {
            var n = Vector3.Cross(p1 - p0, p2 - p0);
            if (n.sqrMagnitude < 1e-14f) return;
            if (Vector3.Dot(n, (p0 + p1 + p2) / 3f - center) < 0f) { var tmp = p1; p1 = p2; p2 = tmp; }
            int s = v.Count;
            foreach (var p in new[] { p0, p1, p2 })
            {
                v.Add(m.MultiplyPoint3x4(p));
                w.Add(new BoneWeight { boneIndex0 = bone, weight0 = 1f });
            }
            tris[sub].Add(s); tris[sub].Add(s + 1); tris[sub].Add(s + 2);
        }
    }

    static Mesh BuildMesh(Rig r, Transform meshT)
    {
        // Pose de enlace: todos los huesos sin rotar.
        foreach (var t in r.all) t.localRotation = Quaternion.identity;
        r.forearm.localPosition = Vector3.zero;

        var mb = new MeshBuilder();
        int Idx(Transform t) => r.all.IndexOf(t);
        Matrix4x4 M(Transform t) => meshT.worldToLocalMatrix * t.localToWorldMatrix;

        // Antebrazo: piel + manga
        mb.Box(Idx(r.forearm), M(r.forearm), 0.11f, ForearmLen + 0.005f, 0.030f, 0.026f, 0.027f, 0.019f);
        mb.Box(Idx(r.forearm), M(r.forearm), -0.20f, 0.125f, 0.040f, 0.036f, 0.036f, 0.032f, sub: 1, bevel: 0.35f);
        // Palma
        mb.Box(Idx(r.hand), M(r.hand), -0.005f, PalmLen, 0.031f, 0.018f, 0.043f, 0.014f, cx: -0.001f);
        // Dedos
        for (int f = 0; f < 4; f++)
            for (int j = 0; j < 3; j++)
            {
                float wdt = FingerWidth[f] * (1f - j * 0.09f) * 0.5f;
                float d = wdt * 0.92f;
                float len = FingerLen[f][j];
                float end = j == 2 ? len : len + 0.002f;
                mb.Box(Idx(r.fingers[f][j]), M(r.fingers[f][j]), -0.002f, end, wdt, d, wdt * (j == 2 ? 0.7f : 0.93f), d * (j == 2 ? 0.7f : 0.93f), cz: -0.002f);
            }
        // Pulgar
        for (int j = 0; j < 3; j++)
        {
            float wdt = (j == 0 ? 0.024f : 0.021f - j * 0.0015f) * 0.5f;
            float len = ThumbLen[j];
            mb.Box(Idx(r.thumb[j]), M(r.thumb[j]), j == 0 ? -0.01f : -0.002f, len + (j == 2 ? 0f : 0.002f), wdt, wdt * 0.9f, wdt * (j == 2 ? 0.7f : 0.92f), wdt * (j == 2 ? 0.65f : 0.85f));
        }

        var mesh = new Mesh { name = "ManoLowPoly_Mesh" };
        mesh.SetVertices(mb.v);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(mb.tris[0], 0);
        mesh.SetTriangles(mb.tris[1], 1);
        mesh.boneWeights = mb.w.ToArray();
        var bind = new Matrix4x4[r.all.Count];
        for (int i = 0; i < bind.Length; i++) bind[i] = r.all[i].worldToLocalMatrix * meshT.localToWorldMatrix;
        mesh.bindposes = bind;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---------- Clips ----------
    static string PathOf(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (var c = t; c != root; c = c.parent) parts.Insert(0, c.name);
        return string.Join("/", parts);
    }

    static AnimationClip BakeClip(Rig r, List<Key> keys, string name, bool includeArm = true, bool loop = false)
    {
        float len = keys[keys.Count - 1].t;
        int frames = Mathf.RoundToInt(len * FPS);
        var bones = r.all;
        var rot = new List<Quaternion>[bones.Count];
        for (int i = 0; i < bones.Count; i++) rot[i] = new List<Quaternion>();
        var pos = new List<Vector3>();

        for (int f = 0; f <= frames; f++)
        {
            float t = f / FPS;
            Apply(r, Sample(keys, t), t);
            for (int i = 0; i < bones.Count; i++)
            {
                var q = bones[i].localRotation;
                if (f > 0 && Quaternion.Dot(rot[i][f - 1], q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                rot[i].Add(q);
            }
            pos.Add(r.forearm.localPosition);
        }

        var clip = new AnimationClip { name = name, frameRate = FPS };
        for (int i = 0; i < bones.Count; i++)
        {
            if (!includeArm && bones[i] == r.forearm) continue;
            string path = PathOf(bones[i], r.root);
            for (int c = 0; c < 4; c++)
            {
                int ci = c;
                Set(clip, path, "m_LocalRotation." + "xyzw"[c], frames, f => rot[i][f][ci]);
            }
        }
        if (includeArm)
        {
            string fp = PathOf(r.forearm, r.root);
            Set(clip, fp, "m_LocalPosition.x", frames, f => pos[f].x);
            Set(clip, fp, "m_LocalPosition.y", frames, f => pos[f].y);
            Set(clip, fp, "m_LocalPosition.z", frames, f => pos[f].z);
        }

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    static void Set(AnimationClip clip, string path, string prop, int frames, Func<int, float> val)
    {
        var ks = new Keyframe[frames + 1];
        float dt = 1f / FPS;
        for (int f = 0; f <= frames; f++)
        {
            int a = Mathf.Max(0, f - 1), b = Mathf.Min(frames, f + 1);
            float tan = (val(b) - val(a)) / ((b - a) * dt);
            ks[f] = new Keyframe(f * dt, val(f), tan, tan);
        }
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), prop), new AnimationCurve(ks));
    }

    // ---------- Guardado ----------
    static T SaveOrReplace<T>(T obj, string path) where T : UnityEngine.Object
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(obj, existing);
            existing.name = System.IO.Path.GetFileNameWithoutExtension(path);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(obj, path);
        return obj;
    }

    static Material GetMat(string path, Color c, float smooth)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        m = new Material(sh);
        m.SetColor("_BaseColor", c);
        m.color = c;
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Glossiness", smooth);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    public const string PrefabPath = Dir + "/ManoLowPoly.prefab";

    [MenuItem("Tools/Manos Low Poly/Generar mano y animaciones")]
    public static void Generate()
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets", "Manos");

        var rig = BuildRig();
        try
        {
            var meshGO = new GameObject("Malla");
            meshGO.transform.SetParent(rig.root, false);
            var mesh = SaveOrReplace(BuildMesh(rig, meshGO.transform), Dir + "/ManoLowPoly_Mesh.asset");

            var piel = GetMat(Dir + "/Mano_Piel.mat", new Color(0.91f, 0.70f, 0.56f), 0.15f);
            var manga = GetMat(Dir + "/Mano_Manga.mat", new Color(0.22f, 0.25f, 0.33f), 0.05f);

            var smr = meshGO.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.sharedMaterials = new[] { piel, manga };
            smr.bones = rig.all.ToArray();
            smr.rootBone = rig.forearm;
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var clipGolpe = SaveOrReplace(BakeClip(rig, KeysGolpe(), "Mano_GolpeAbajo"), Dir + "/Mano_GolpeAbajo.anim");
            var clipAbrir = SaveOrReplace(BakeClip(rig, KeysAbrir(), "Mano_CerrarAbrir"), Dir + "/Mano_CerrarAbrir.anim");

            string ctrlPath = Dir + "/ManoLowPoly.controller";
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            var sm = ctrl.layers[0].stateMachine;
            AnimatorState Ensure(string name, Motion m)
            {
                foreach (var cs in sm.states) if (cs.state.name == name) { cs.state.motion = m; return cs.state; }
                var s = sm.AddState(name); s.motion = m; return s;
            }
            var sGolpe = Ensure(ManoDemo.EstadoGolpe, clipGolpe);
            Ensure(ManoDemo.EstadoAbrir, clipAbrir);
            sm.defaultState = sGolpe;
            EditorUtility.SetDirty(ctrl);

            // El prefab solo se construye la primera vez: si ya existe no se rehace,
            // para no romper las manos que ya estén colocadas en las escenas.
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                // Pose inicial del prefab = primer fotograma de GolpeAbajo
                Apply(rig, Sample(KeysGolpe(), 0f), 0f);
                var anim = rig.root.gameObject.AddComponent<Animator>();
                anim.runtimeAnimatorController = ctrl;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                PrefabUtility.SaveAsPrefabAsset(rig.root.gameObject, PrefabPath);
            }
            AssetDatabase.SaveAssets();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rig.root.gameObject);
        }
        GenerarJugador();
        Debug.Log("Mano low poly generada en " + Dir + ".");
    }

    public const string ControllerJugadorPath = Dir + "/ManoJugador.controller";

    /// <summary>Clips Idle / Disparo / PunoCamara, su controlador, y el prefab pasa a usar ManoJugador.</summary>
    [MenuItem("Tools/Manos Low Poly/Generar animaciones del jugador")]
    public static void GenerarJugador()
    {
        var rig = BuildRig();
        AnimationClip idle, disparo, puno, abrir, golpeE;
        try
        {
            // Mano izquierda (E): la animación del vídeo 23.27.47 tal cual, con su movimiento de brazo.
            wiggleVideo = true;
            try
            {
                abrir = SaveOrReplace(BakeClip(rig, KeysAbrir(), "Mano_CerrarAbrir"), Dir + "/Mano_CerrarAbrir.anim");
                golpeE = SaveOrReplace(BakeClip(rig, KeysGolpe(), "Mano_GolpeAbajo"), Dir + "/Mano_GolpeAbajo.anim");
            }
            finally { wiggleVideo = false; }
            idle = SaveOrReplace(BakeClip(rig, KeysIdle(), "Mano_Idle", includeArm: false, loop: true), Dir + "/Mano_Idle.anim");
            disparo = SaveOrReplace(BakeClip(rig, KeysDisparo(), "Mano_Disparo", includeArm: false), Dir + "/Mano_Disparo.anim");
            puno = SaveOrReplace(BakeClip(rig, KeysPunoCamara(), "Mano_PunoCamara", includeArm: false), Dir + "/Mano_PunoCamara.anim");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rig.root.gameObject);
        }

        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerJugadorPath);
        if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerJugadorPath);
        var sm = ctrl.layers[0].stateMachine;
        foreach (var cs in sm.states) sm.RemoveState(cs.state);
        var sIdle = sm.AddState(ManoJugador.EstadoIdle, new Vector3(300, 0, 0)); sIdle.motion = idle;
        var sDisp = sm.AddState(ManoJugador.EstadoDisparo, new Vector3(300, 100, 0)); sDisp.motion = disparo;
        var sPuno = sm.AddState(ManoJugador.EstadoPuno, new Vector3(300, -100, 0)); sPuno.motion = puno;
        var sAbrir = sm.AddState(ManoJugador.EstadoAbrir, new Vector3(550, -100, 0)); sAbrir.motion = abrir;
        var sGolpeE = sm.AddState(ManoJugador.EstadoGolpe, new Vector3(550, 0, 0)); sGolpeE.motion = golpeE;
        sm.defaultState = sIdle;
        foreach (var s in new[] { sDisp, sPuno, sAbrir, sGolpeE })
        {
            var tr = s.AddTransition(sIdle);
            tr.hasExitTime = true; tr.exitTime = 1f;
            tr.hasFixedDuration = true; tr.duration = s == sAbrir || s == sGolpeE ? 0.35f : 0.15f;
        }
        EditorUtility.SetDirty(ctrl);

        // Se edita el prefab existente (no se rehace) para que las instancias de las escenas conserven su colocación.
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var anim = root.GetComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var demo = root.GetComponent<ManoDemo>();
            if (demo != null) UnityEngine.Object.DestroyImmediate(demo);
            var mj = root.GetComponent<ManoJugador>();
            if (mj == null) mj = root.AddComponent<ManoJugador>();
            mj.controlador = ctrl;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Manos Low Poly/Crear escena de prueba")]
    public static void CreateDemoScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Generate();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var cam = Camera.main;
        cam.transform.SetPositionAndRotation(new Vector3(0, 1.6f, 0), Quaternion.identity);
        cam.nearClipPlane = 0.01f;
        cam.fieldOfView = 60f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.6f, 0.68f);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var hand = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        hand.transform.SetParent(cam.transform, false);
        // La escena de prueba enseña las animaciones de los vídeos (teclas 1/2).
        UnityEngine.Object.DestroyImmediate(hand.GetComponent<ManoJugador>());
        hand.GetComponent<Animator>().runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Dir + "/ManoLowPoly.controller");
        hand.AddComponent<ManoDemo>();

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.transform.localScale = Vector3.one * 3f;

        EditorSceneManager.SaveScene(scene, Dir + "/ManosDemo.unity");
        Selection.activeGameObject = hand;
    }

    // ---------- Previsualización a PNG (escena de previsualización aislada, no toca la escena abierta) ----------
    const string PreviewDir = "ManoPreview"; // fuera de Assets

    [InitializeOnLoadMethod]
    static void AutoRunIfFlagged()
    {
        string flag = PreviewDir + "/run.flag";
        if (!System.IO.File.Exists(flag)) return;
        EditorApplication.delayCall += () =>
        {
            if (!System.IO.File.Exists(flag)) return;
            System.IO.File.Delete(flag);
            try { GenerarJugador(); RenderPreview(); }
            catch (Exception e) { System.IO.File.WriteAllText(PreviewDir + "/error.txt", e.ToString()); throw; }
        };
    }

    [MenuItem("Tools/Manos Low Poly/Renderizar previsualización (PNG)")]
    public static void RenderPreview()
    {
        System.IO.Directory.CreateDirectory(PreviewDir);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var scene = EditorSceneManager.NewPreviewScene();
        var rt = new RenderTexture(480, 270, 24);
        var rtc = new RenderTexture(300, 300, 24);
        try
        {
            var go = UnityEngine.Object.Instantiate(prefab);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);

            var lightGO = new GameObject("Luz");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.3f;
            lightGO.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGO, scene);

            var camGO = new GameObject("Cam");
            var cam = camGO.AddComponent<Camera>();
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGO, scene);
            cam.scene = scene; cam.fieldOfView = 60f; cam.nearClipPlane = 0.01f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.6f, 0.68f);

            var hand = go.transform.Find("Antebrazo/Mano");
            // El skinning no se recalcula al renderizar a mano en una escena de previsualización,
            // así que se "hornea" la malla en cada fotograma y se pinta con un MeshRenderer normal.
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            smr.enabled = false;
            var baked = new Mesh();
            var bakedGO = new GameObject("Horneada");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(bakedGO, scene);
            bakedGO.AddComponent<MeshFilter>().sharedMesh = baked;
            bakedGO.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
            foreach (var clip in new[] { "Mano_GolpeAbajo", "Mano_CerrarAbrir", "Mano_Idle", "Mano_Disparo", "Mano_PunoCamara" })
            {
                var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "/" + clip + ".anim");
                if (c == null) continue;
                int n = 0;
                for (float t = 0f; t <= c.length + 0.001f; t += 0.1f, n++)
                {
                    c.SampleAnimation(go, t);
                    smr.BakeMesh(baked);
                    bakedGO.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                    cam.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    Shot(cam, rt, $"{PreviewDir}/{clip}_fp_{n:000}.png");
                    var center = hand.position + hand.up * 0.06f;
                    cam.transform.position = center + new Vector3(-0.25f, 0.1f, -0.2f);
                    cam.transform.LookAt(center);
                    Shot(cam, rtc, $"{PreviewDir}/{clip}_cu_{n:000}.png");
                }
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(rtc);
        }
        Debug.Log("Previsualización guardada en " + System.IO.Path.GetFullPath(PreviewDir));
    }

    static void Shot(Camera cam, RenderTexture rt, string file)
    {
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        cam.targetTexture = null;
    }

    [MenuItem("Tools/Manos Low Poly/Añadir mano a la cámara seleccionada")]
    public static void AddToSelectedCamera()
    {
        var sel = Selection.activeGameObject;
        var cam = sel != null ? sel.GetComponentInChildren<Camera>() : Camera.main;
        if (cam == null) { Debug.LogWarning("Selecciona una cámara."); return; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) { Generate(); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); }
        var hand = (GameObject)PrefabUtility.InstantiatePrefab(prefab, cam.transform);
        hand.transform.localPosition = Vector3.zero;
        hand.transform.localRotation = Quaternion.identity;
        if (cam.nearClipPlane > 0.02f) { Undo.RecordObject(cam, "Near clip"); cam.nearClipPlane = 0.02f; }
        Undo.RegisterCreatedObjectUndo(hand, "Añadir mano");
        Selection.activeGameObject = hand;
    }
}
