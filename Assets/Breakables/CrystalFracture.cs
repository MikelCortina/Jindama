using System;
using System.Collections.Generic;

/// <summary>
/// Fractura de cristal de un cubo: puntos al azar en el cubo (esquinas, aristas, caras e interior) unidos en
/// tetraedros con una triangulación de Delaunay 3D (Bowyer-Watson). Todas las piezas son pirámides de base
/// triangular, de tamaños y formas distintas, que rellenan el cubo exacto sin huecos.
///
/// No depende de Unity (solo System) para poder probarlo fuera del editor.
/// </summary>
public static class CrystalFracture
{
    public struct P
    {
        public double x, y, z;
        public P(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public static P operator +(P a, P b) { return new P(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static P operator -(P a, P b) { return new P(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static P operator *(P a, double s) { return new P(a.x * s, a.y * s, a.z * s); }
        public double Dot(P b) { return x * b.x + y * b.y + z * b.z; }
        public P Cross(P b) { return new P(y * b.z - z * b.y, z * b.x - x * b.z, x * b.y - y * b.x); }
        public double Sqr() { return x * x + y * y + z * z; }
    }

    /// <summary>
    /// Puntos del cubo [-size/2, size/2]³. <paramref name="detail"/> ≈ piezas por lado.
    /// Repartidos al azar con una distancia mínima entre ellos (así no salen astillas diminutas).
    /// </summary>
    public static List<P> CubePoints(double size, int detail, int seed)
    {
        var rng = new Random(seed);
        int n = Math.Max(1, detail);
        double h = size * 0.5, spacing = size / n;
        var pts = new List<P>();

        // Esquinas.
        for (int i = 0; i < 8; i++)
            pts.Add(new P((i & 1) == 0 ? -h : h, (i & 2) == 0 ? -h : h, (i & 4) == 0 ? -h : h));

        // Aristas: n-1 puntos por arista, a distancias al azar.
        for (int axis = 0; axis < 3; axis++)
        for (int s = 0; s < 4; s++)
        {
            double a = (s & 1) == 0 ? -h : h, b = (s & 2) == 0 ? -h : h;
            for (int i = 1; i < n; i++)
            {
                double t = -h + spacing * (i + (rng.NextDouble() - 0.5) * 0.7);
                pts.Add(axis == 0 ? new P(t, a, b) : axis == 1 ? new P(a, t, b) : new P(a, b, t));
            }
        }

        // Caras: (n-1)² puntos por cara, al azar dentro de la cara.
        for (int axis = 0; axis < 3; axis++)
        for (int side = 0; side < 2; side++)
        {
            double fixedCoord = side == 0 ? -h : h;
            Scatter(pts, rng, (n - 1) * (n - 1), spacing * 0.6, delegate
            {
                double u = -h + spacing * 0.3 + rng.NextDouble() * (size - spacing * 0.6);
                double v = -h + spacing * 0.3 + rng.NextDouble() * (size - spacing * 0.6);
                return axis == 0 ? new P(fixedCoord, u, v) : axis == 1 ? new P(u, fixedCoord, v) : new P(u, v, fixedCoord);
            });
        }

        // Interior: (n-1)³ puntos al azar, sin pegarse a las caras.
        Scatter(pts, rng, (n - 1) * (n - 1) * (n - 1), spacing * 0.65, delegate
        {
            double m = spacing * 0.3, w = size - 2 * m;
            return new P(-h + m + rng.NextDouble() * w, -h + m + rng.NextDouble() * w, -h + m + rng.NextDouble() * w);
        });

        return pts;
    }

    /// <summary>Añade hasta <paramref name="count"/> puntos al azar que estén a más de minDist de todos los demás.</summary>
    static void Scatter(List<P> pts, Random rng, int count, double minDist, Func<P> sample)
    {
        double min2 = minDist * minDist;
        int added = 0;
        for (int attempt = 0; attempt < count * 40 && added < count; attempt++)
        {
            P c = sample();
            bool ok = true;
            for (int i = 0; i < pts.Count && ok; i++) ok = (pts[i] - c).Sqr() >= min2;
            if (!ok) continue;
            pts.Add(c);
            added++;
        }
    }

    class Tet
    {
        public int a, b, c, d;
        public P center;
        public double r2;
        public bool alive = true;
    }

    /// <summary>
    /// Triangulación de Delaunay 3D. Devuelve tetraedros (4 índices de <paramref name="pts"/> cada uno) con volumen
    /// positivo en las posiciones originales; los planos (p.ej. 4 puntos de una misma cara del cubo) se descartan.
    /// </summary>
    public static List<int[]> Tetrahedralize(List<P> pts, int seed)
    {
        int n = pts.Count;
        // Copia con una perturbación minúscula: evita empates (puntos coplanarios o cocirculares) que rompen el algoritmo.
        var rng = new Random(seed ^ 0x5bd1e995);
        P lo = pts[0], hi = pts[0];
        foreach (var p in pts)
        {
            lo = new P(Math.Min(lo.x, p.x), Math.Min(lo.y, p.y), Math.Min(lo.z, p.z));
            hi = new P(Math.Max(hi.x, p.x), Math.Max(hi.y, p.y), Math.Max(hi.z, p.z));
        }
        double extent = Math.Sqrt((hi - lo).Sqr());
        var q = new List<P>(n + 4);
        foreach (var p in pts)
            q.Add(p + new P(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5) * (extent * 1e-7));

        // Supertetraedro que contiene todo.
        P mid = (lo + hi) * 0.5;
        double k = extent * 20;
        q.Add(mid + new P(1, 1, 1) * k);
        q.Add(mid + new P(-1, -1, 1) * k);
        q.Add(mid + new P(-1, 1, -1) * k);
        q.Add(mid + new P(1, -1, -1) * k);

        var tets = new List<Tet> { MakeTet(q, n, n + 1, n + 2, n + 3) };
        var faceCount = new Dictionary<long, int>();
        var faceTri = new Dictionary<long, int[]>();
        long M = n + 4;

        // Orden de inserción al azar (mejor comportamiento medio).
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        for (int i = n - 1; i > 0; i--) { int j = rng.Next(i + 1); int t = order[i]; order[i] = order[j]; order[j] = t; }

        foreach (int pi in order)
        {
            P p = q[pi];
            faceCount.Clear();
            faceTri.Clear();
            int bad = 0;
            foreach (var t in tets)
            {
                if (!t.alive || (p - t.center).Sqr() >= t.r2) continue;
                t.alive = false;
                bad++;
                AddFace(faceCount, faceTri, M, t.a, t.b, t.c);
                AddFace(faceCount, faceTri, M, t.a, t.b, t.d);
                AddFace(faceCount, faceTri, M, t.a, t.c, t.d);
                AddFace(faceCount, faceTri, M, t.b, t.c, t.d);
            }
            if (bad == 0) continue; // no debería pasar (está dentro del supertetraedro)

            foreach (var kv in faceCount)
            {
                if (kv.Value != 1) continue; // cara interior de la cavidad
                var f = faceTri[kv.Key];
                tets.Add(MakeTet(q, f[0], f[1], f[2], pi));
            }

            // Compactar de vez en cuando.
            if (tets.Count > 4 * n + 64) tets.RemoveAll(delegate (Tet t) { return !t.alive; });
        }

        // Quitar los que tocan el supertetraedro y los planos; orientar con volumen positivo.
        var result = new List<int[]>();
        double minVol = Math.Pow(extent, 3) * 1e-9;
        foreach (var t in tets)
        {
            if (!t.alive || t.a >= n || t.b >= n || t.c >= n || t.d >= n) continue;
            double vol = SignedVolume(pts[t.a], pts[t.b], pts[t.c], pts[t.d]);
            if (Math.Abs(vol) < minVol) continue;
            result.Add(vol > 0 ? new[] { t.a, t.b, t.c, t.d } : new[] { t.a, t.c, t.b, t.d });
        }
        return result;
    }

    public static double SignedVolume(P a, P b, P c, P d)
    {
        return (b - a).Dot((c - a).Cross(d - a)) / 6.0;
    }

    static Tet MakeTet(List<P> q, int a, int b, int c, int d)
    {
        var t = new Tet { a = a, b = b, c = c, d = d };
        P p0 = q[a], u = q[b] - p0, v = q[c] - p0, w = q[d] - p0;
        double den = 2.0 * u.Dot(v.Cross(w));
        if (Math.Abs(den) < 1e-300)
        {
            // Degenerado: cualquier punto nuevo lo "contiene", así se rehace enseguida.
            t.center = p0;
            t.r2 = double.MaxValue;
            return t;
        }
        P num = v.Cross(w) * u.Sqr() + w.Cross(u) * v.Sqr() + u.Cross(v) * w.Sqr();
        P off = num * (1.0 / den);
        t.center = p0 + off;
        t.r2 = off.Sqr();
        return t;
    }

    static void AddFace(Dictionary<long, int> count, Dictionary<long, int[]> tri, long M, int a, int b, int c)
    {
        int x = a, y = b, z = c, s;
        if (x > y) { s = x; x = y; y = s; }
        if (y > z) { s = y; y = z; z = s; }
        if (x > y) { s = x; x = y; y = s; }
        long key = (x * M + y) * M + z;
        int prev;
        count.TryGetValue(key, out prev);
        count[key] = prev + 1;
        if (prev == 0) tri[key] = new[] { a, b, c };
    }
}
