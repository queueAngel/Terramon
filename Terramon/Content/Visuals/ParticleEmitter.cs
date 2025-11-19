using ReLogic.Content;
using System.Runtime.InteropServices;
using Terraria.GameContent;
using Terraria.Graphics;

namespace Terramon.Content.Visuals;

public sealed class ParticleEmitter(ParticleSchema schema, Asset<Texture2D> tex)
{
    public ParticleSchema Schema = schema;
    public Asset<Texture2D> Texture = tex;
    public byte TimeLeft;
    public bool KeptAlive;
    public bool Additive;
    public List<Particle> Particles = [];
    public int Emit(Vector2 position, Vector2 velocity, float rotation = 0f, byte lifetime = 60)
    {
        if (Main.dedServ)
            return 0;
        Particle newP = new()
        {
            Position = position,
            Velocity = velocity,
            Rotation = rotation,
            TimeLeft = lifetime,
            SpawnParameters = new(lifetime)
        };
        int idx = 0;
        foreach (ref var p in CollectionsMarshal.AsSpan(Particles))
        {
            if (p.TimeLeft == 0)
            {
                var oldDrawer = p.Trail;
                p = newP;
                oldDrawer.Set(position);
                p.Trail = oldDrawer;
                return idx;
            }
            idx++;
        }
        idx = Particles.Count;
        var trail = Schema.Trail;
        if (trail.Style != ParticleTrailStyle.None)
        {
            var trailDrawer = new ParticleTrailDrawer(trail);
            trailDrawer.Set(position);
            newP.Trail = trailDrawer;
        }
        Particles.Add(newP);
        return idx;
    }
    public void UpdateParticles()
    {
        if (KeptAlive)
        {
            TimeLeft = 2;
            KeptAlive = false;
        }
        else if (TimeLeft <= 2)
        {
            if (Particles.Count != 0)
                TimeLeft = Math.Max(TimeLeft, Particles.Max(p => p.TimeLeft));
        }
        foreach (ref var p in CollectionsMarshal.AsSpan(Particles))
        {
            if (p.TimeLeft == 0)
                continue;
            p.Trail?.Record(p.Position);
            AI(ref p);
            p.Position += p.Velocity;
            p.Rotation += p.AngularVelocity;
        }
    }
    public void DrawParticles(SpriteBatch sb)
    {
        foreach (ref var p in CollectionsMarshal.AsSpan(Particles))
        {
            if (p.TimeLeft == 0)
                continue;
            p.Trail?.Draw();
            p.DrawCommon(this, sb, TextureAssets.Logo.Value, Color.White, TextureAssets.Logo.Size() * 0.5f, 0f);
            if (Texture != null)
                p.DrawCommon(this, sb, Texture.Value, GetAlpha(in p), null, p.Rotation);
        }
    }
    public void AI(ref Particle p)
    {

    }
    public Color GetAlpha(in Particle p) => Lighting.GetColor(p.Position.ToTileCoordinates());
}

public sealed class ParticleTrailDrawer(ParticleTrail schema)
{
    private static readonly VertexStrip Strip = new();
    public ParticleTrail Schema = schema;
    public Vector2[] Positions = new Vector2[schema.Length];
    public void Set(Vector2 allPositions) => Array.Fill(Positions, allPositions);
    public void Record(Vector2 position)
    {
        for (int i = Positions.Length - 2; i >= 0; i--)
            Positions[i + 1] = Positions[i];
        Positions[0] = position;
    }
    public void Draw()
    {
        Strip.PrepareStrip(Positions, Rotations(Positions), (p) => Color.White, (p) => Schema.Size);
        Strip.DrawTrail();
    }
    /// <summary>
    ///     INTO THE DEPTHS SWEEP!!
    ///     INTO THE DEPTHS SWEEP!!
    ///     INTO THE DEPTHS SWEEP!!
    /// </summary>
    public static float[] Rotations(Vector2[] positions)
    {
        int res = positions.Length;
        float[] rotations = new float[res];
        for (int i = 0; i < res; i++)
        {
            Vector2 cur = positions[i];
            Vector2 next = i < res - 1 ? positions[i + 1] : positions[i];
            Vector2 diff = next - cur;

            if (diff == Vector2.Zero)
                rotations[i] = i > 0 ? rotations[i - 1] : 0;
            else
                rotations[i] = diff.ToRotation();
        }
        return rotations;
    }
}

public struct Particle()
{
    public Vector2 Position;
    public Vector2 Velocity;
    public float Rotation;
    public float AngularVelocity;
    public float Scale = 1f;
    public byte Opacity = byte.MaxValue;
    public byte TimeLeft;
    public ParticleSpawnParameters SpawnParameters;
    public Vector2[] OldPositions;
    public ParticleTrailDrawer Trail;
    public readonly void DrawCommon(
        ParticleEmitter source,
        SpriteBatch sb,
        Texture2D tex,
        Color color = default,
        Vector2? origin = null,
        float rotation = 0f)
    {
        if (tex is null)
            return;

        if (color.PackedValue == 0u)
            color = source.GetAlpha(in this);
        if (source.Additive)
            color.A = 0;

        var orig = origin ?? tex.Size() * 0.5f;
        sb.Draw(tex, Position, null, color, rotation, orig, Scale, SpriteEffects.None, 0f);
    }
}

public readonly record struct ParticleSpawnParameters(byte TimeLeft);
