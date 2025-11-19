using System.Text.Json;
using System.Text.Json.Serialization;

namespace Terramon.Content.Visuals;

[Autoload(false)]
public sealed class VisualsLoader : ModSystem
{
    public static Dictionary<string, ParticleEmitter> EmittersByName = [];
    public static ParticleEmitter[] EmittersByID;
    public override void Load()
    {
        // Load effect schemas from file
        using var jsonStream = Mod.GetFileStream($"Assets/Data/VFX.json");

        var json = JsonDocument.Parse(jsonStream);

        var emitters = new List<ParticleEmitter>();
        foreach (var particle in json.RootElement.GetProperty("particles").EnumerateObject())
        {
            var name = particle.Name;
            var body = particle.Value;

            ParticleProperties props;
            ParticleMovement mov;
            ParticleTrail tr;

            if (body.TryGetProperty("properties", out var properties))
                props = JsonSerializer.Deserialize<ParticleProperties>(properties);
            else
                props = new();

            if (body.TryGetProperty("movement", out var movement))
                mov = JsonSerializer.Deserialize<ParticleMovement>(movement);
            else
                mov = new();

            if (body.TryGetProperty("trail", out var trail))
                tr = JsonSerializer.Deserialize<ParticleTrail>(trail);
            else
                tr = new();

            var schema = new ParticleSchema(props, mov, tr);
            var emitter = new ParticleEmitter(schema, null);
            emitters.Add(emitter);
            EmittersByName.Add(name, emitter);
        }
        EmittersByID = emitters.ToArray();

        On_Main.DrawCachedProjs += static (orig, self, projCache, startSpriteBatch) =>
        {
            orig(self, projCache, startSpriteBatch);

            if (projCache != Main.instance.DrawCacheProjsOverPlayers)
                return;

            if (!startSpriteBatch)
                Main.spriteBatch.End();

            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);

            DrawParticles(Main.spriteBatch);

            Main.spriteBatch.End();

            if (!startSpriteBatch)
                Main.spriteBatch.Begin(default, default, SamplerState.PointClamp, default, RasterizerState.CullNone, default, Main.GameViewMatrix.TransformationMatrix);
        };
    }
    public override void PreUpdateDusts()
    {
        for (int i = 0; i < EmittersByID.Length; i++)
        {
            var emitter = EmittersByID[i];
            emitter.UpdateParticles();
        }
    }
    public static void DrawParticles(SpriteBatch sb)
    {
        for (int i = 0; i < EmittersByID.Length; i++)
        {
            var emitter = EmittersByID[i];
            emitter.DrawParticles(sb);
        }
    }
    public override void Unload()
    {
        EmittersByName = null;
        EmittersByID = null;
    }
}

public sealed record ParticleSchema(
    ParticleProperties Properties = null,
    ParticleMovement Movement = null,
    ParticleTrail Trail = null);
public sealed record ParticleProperties(bool Pixelated = false, byte FadeIn = 0, byte FadeOut = 0);
public sealed record ParticleMovement(
    ParticleMovementStyle Style = ParticleMovementStyle.None,
    ParticlePathShape PathShape = ParticlePathShape.Line,
    float Start = 0f, float End = 1f);
public sealed record ParticleTrail(
    ParticleTrailStyle Style = ParticleTrailStyle.None,
    byte Length = 0, float Size = 0f,
    [property: JsonConverter(typeof(ColorParser))] Color Color = default,
    ParticleTrailTaper Taper = ParticleTrailTaper.None,
    Ease TaperFunction = Ease.None);
public sealed class ColorParser : JsonConverter<Color>
{
    private static Dictionary<uint, string> ColorNames = [];
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new InvalidOperationException();
        var str = reader.GetString().TrimStart('#');
        if (uint.TryParse(str, System.Globalization.NumberStyles.HexNumber, null, out var packed))
            return new Color(packed | (255u << 24));

        var getter = typeof(Color).GetMethod("get_" + str, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
        if (getter != null)
            return (Color)getter.Invoke(null, null);
        throw new InvalidOperationException();
    }
    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
    {
        if (ColorNames is null)
        {
            ColorNames = [];
            var props = typeof(Color).GetProperties(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            for (int i = 0; i < props.Length; i++)
            {
                var prop = props[i];
                var color = (Color)prop.GetValue(null, null);
                ColorNames.Add(color.PackedValue, prop.Name);
            }
        }
        if (ColorNames.TryGetValue(value.PackedValue, out var name))
            writer.WriteStringValue(name);
        else
            writer.WriteStringValue("#" + value.PackedValue.ToString("X6"));
    }
}
public enum ParticleMovementStyle : byte
{
    None,
    TracePath,
    TracePathLoop,
    TracePathPingPong,
}
public enum ParticlePathShape : byte
{
    Line,
    BezierCurve,
    Circle,
    Triangle,
    Square,
}
public enum ParticleTrailStyle : byte
{
    None,
    AfterImages,
    Mesh,
}
public enum ParticleTrailTaper : byte
{
    None,
    Both,
    Target,
    Start,
}
