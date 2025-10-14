using Hjson;
using Newtonsoft.Json.Linq;
using ReLogic.Content;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using Terramon.Content.NPCs;
using Terramon.Content.Projectiles;
using Terramon.Content.Tiles.Banners;
using Terramon.Core.Abstractions;
using Terramon.Core.NPCComponents;
using Terraria.Graphics.Shaders;

namespace Terramon.Core.Loaders;

/// <summary>
///     A system that loads handles the manual loading of Pokémon NPCs and pet projectiles.
/// </summary>
[Autoload(false)]
public class PokemonEntityLoader : ModSystem
{
    public static Dictionary<ushort, Asset<Texture2D>> GlowTextureCache { get; private set; }
    public static Dictionary<ushort, Asset<Texture2D>> ShinyGlowTextureCache { get; private set; }
    public static Dictionary<ushort, Texture2D> HighlightTextures;
    public static Dictionary<ushort, int> IDToNPCType { get; private set; }
    public static Dictionary<ushort, int> IDToPetType { get; private set; }
    public static Dictionary<ushort, int> IDToBannerType { get; private set; }
    public static Dictionary<ushort, Action<NPC>> NPCSchemaCache { get; private set; }
    public static Dictionary<ushort, JToken> PetSchemaCache { get; private set; }
    private static BitArray HasGenderDifference { get; set; }
    private static BitArray HasPetExclusiveTexture { get; set; }
    private static List<PokeBannerItem> ShinyBanners { get; set; }
    private static Dictionary<ushort, Dictionary<Type, Dictionary<FieldInfo, object>>> _tokenized = [];

    public override void OnModLoad()
    {
        // The initialization of these arrays is done here rather than in Load to avoid a null ref exception reading HighestPokemonID
        var highestPokemonID = Terramon.HighestPokemonID;
        HasGenderDifference = new BitArray(highestPokemonID);
        HasPetExclusiveTexture = new BitArray(highestPokemonID);

        // Load the fade shader for Pokémon
        if (!Main.dedServ)
        {
            const string Effects = "Assets/Effects/";
            GameShaders.Misc[$"{nameof(Terramon)}FadeToColor"] =
                new MiscShaderData(Mod.Assets.Request<Effect>(Effects + "FadeToColor"), "FadePass");
            GameShaders.Misc[$"{nameof(Terramon)}Outline"] =
                new MiscShaderData(Mod.Assets.Request<Effect>(Effects + "Outline"), "ShaderPass");
        }

        // Start a stopwatch to measure the time taken to load Pokémon entities
        //var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        foreach (var (id, pokemon) in Terramon.DatabaseV2.Pokemon)
        {
            if (id > Terramon.MaxPokemonIDToLoad) continue;
            if (!HjsonSchemaExists(pokemon.Identifier)) continue;
            LoadEntities(id, pokemon);
        }
        
        // Done in a second pass to add them all after the standard banners are loaded
        LoadShinyBanners();

        /*stopwatch.Stop();
        Mod.Logger.Info($"Loaded Pokémon entities in {stopwatch.ElapsedMilliseconds}ms");*/
    }

    public override void PostSetupContent()
    {
        foreach (var (id, pokemon) in Terramon.DatabaseV2.Pokemon)
        {
            if (id > Terramon.MaxPokemonIDToLoad) continue;
            if (!HjsonSchemaExists(pokemon.Identifier)) continue;
            CompilePokemon(id, pokemon);
        }
        _tokenized = null;
    }
    private static void CompilePokemon(ushort id, DatabaseV2.PokemonSchema schema)
    {
        // now begin translation to IL

        if (_tokenized.TryGetValue(id, out var everything) && everything.Count != 0)
        {
            NPC dummy = new();
            dummy.SetDefaults(IDToNPCType[id]);
            var globals = GetGlobals(dummy);
            var globalsField = typeof(NPC).GetField("_globals", BindingFlags.Instance | BindingFlags.NonPublic);

            DynamicMethod dynamic = new($"Build{schema.Identifier}NPC", null, [typeof(NPC)], true);
            var c = dynamic.GetILGenerator();

            c.Emit(OpCodes.Ldarg_0); // load the NPC

            for (var i = 0; i < everything.Count - 1; i++)
                c.Emit(OpCodes.Dup); // get it a couple extra times
            foreach ((var componentType, var componentActions) in everything)
            {
                int compIndex = Array.FindIndex(globals, g => g.GetType() == componentType);
                if (compIndex == -1)
                {
                    Terramon.Instance.Logger.Warn($"Index for global {componentType.Name} wasn't found for Pokémon {schema.Identifier}.");
                    continue;
                }

                c.Emit(OpCodes.Ldfld, globalsField); // load the globals array
                c.Emit(OpCodes.Ldc_I4, compIndex); // load the index of the component in the array
                c.Emit(OpCodes.Ldelem_Ref); // load the component from the array

                // get it twice again to do the enabling stuff
                // this is only done once if the only thing that needs to be done is enabling the component
                c.Emit(OpCodes.Dup);
                if (componentActions != null)
                    c.Emit(OpCodes.Dup);
                c.Emit(OpCodes.Ldc_I4_1); // true
                c.Emit(OpCodes.Stfld, typeof(NPCComponent).GetField("_enabled", BindingFlags.Instance | BindingFlags.NonPublic)); // set _enabled to true
                c.Emit(OpCodes.Ldarg_0); // load the NPC in again
                c.Emit(OpCodes.Callvirt, typeof(NPCComponent).GetMethod("OnEnabled", BindingFlags.Instance | BindingFlags.NonPublic)); // call OnEnabled
                if (componentActions is null)
                    continue; // if we don't have any component actions (setting fields) then go to the next one
                for (var i = 0; i < componentActions.Count - 1; i++)
                    c.Emit(OpCodes.Dup); // for each component action, duplicate the component
                foreach (var componentAction in componentActions)
                {
                    // emit the object that will be stored in the thing
                    switch (componentAction.Value)
                    {
                        case bool b:
                            c.Emit(OpCodes.Ldc_I4, b ? 1 : 0);
                            break;
                        case int i:
                            c.Emit(OpCodes.Ldc_I4, i);
                            break;
                        case float f:
                            c.Emit(OpCodes.Ldc_R4, f);
                            break;
                        case Enum e:
                            c.Emit(OpCodes.Ldc_I4, Convert.ToInt32(e));
                            break;
                        case string s:
                            c.Emit(OpCodes.Ldstr, s);
                            break;
                        case Vector3 v3:
                            c.Emit(OpCodes.Ldc_R4, v3.X);
                            c.Emit(OpCodes.Ldc_R4, v3.Y);
                            c.Emit(OpCodes.Ldc_R4, v3.Z);
                            c.Emit(OpCodes.Newobj, typeof(Vector3).GetConstructor([typeof(float), typeof(float), typeof(float)]));
                            break;
                    }
                    c.Emit(OpCodes.Stfld, componentAction.Key); // store the object in the thing
                }
            }
            c.Emit(OpCodes.Ret); // end the method
            NPCSchemaCache[id] = dynamic.CreateDelegate<Action<NPC>>(); // make it usable
        }
    }
    private bool HjsonSchemaExists(string identifier)
    {
        return Mod.FileExists($"Content/Pokemon/{identifier}.hjson");
    }

    /// <summary>
    ///     Creates an NPC and pet projectile for the given Pokémon and loads them as mod content.
    /// </summary>
    private void LoadEntities(ushort id, DatabaseV2.PokemonSchema schema)
    {
        // Load corresponding schema from HJSON file
        var hjsonStream = Mod.GetFileStream($"Content/Pokemon/{schema.Identifier}.hjson");
        using var hjsonReader = new StreamReader(hjsonStream);
        var jsonText = HjsonValue.Load(hjsonReader).ToString();
        hjsonReader.Close();
        var hjsonSchema = JObject.Parse(jsonText);

        // Load glowmask textures if they exist
        if (ModContent.RequestIfExists<Texture2D>($"Terramon/Assets/Pokemon/{schema.Identifier}_Glow", out var glowTex))
            GlowTextureCache[id] = glowTex;
        if (ModContent.RequestIfExists<Texture2D>($"Terramon/Assets/Pokemon/{schema.Identifier}_S_Glow",
                out var shinyGlowTex))
            ShinyGlowTextureCache[id] = shinyGlowTex;

        // Check if this Pokémon has a gender difference (alternate texture)
        HasGenderDifference[id - 1] = ModContent.HasAsset($"Terramon/Assets/Pokemon/{schema.Identifier}F");

        // Check if this Pokémon has a pet-exclusive texture
        HasPetExclusiveTexture[id - 1] = ModContent.HasAsset($"Terramon/Assets/Pokemon/{schema.Identifier}_Pet");

        // Get common components
        var commonSchema = hjsonSchema.GetValue("Common");

        // Load Pokémon NPC
        var npc = new PokemonNPC(id, schema);
        Mod.AddContent(npc);
        IDToNPCType.Add(id, npc.NPC.type);
        LoadPokemonNPC(id, hjsonSchema, commonSchema);

        // Load Pokémon pet projectile
        if (hjsonSchema.TryGetValue("Projectile", out var petSchema))
        {
            // Add common components to pet schema
            if (commonSchema != null)
                foreach (var kvp in commonSchema.Children<JProperty>())
                    petSchema[kvp.Name] ??= kvp.Value;

            PetSchemaCache.Add(id, petSchema);
            var pet = new PokemonPet(id, schema);
            Mod.AddContent(pet);
            IDToPetType.Add(id, pet.Projectile.type);
        }
        
        // Load Pokémon banner
        if (ModContent.HasAsset($"Terramon/Assets/Tiles/Banners/{schema.Identifier}Banner"))
            LoadBanner(id, schema);
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_globals")]
    public static extern ref GlobalNPC[] GetGlobals(NPC instance);
    private void LoadPokemonNPC(ushort id, JObject hjsonSchema, JToken commonSchema)
    {
        if (hjsonSchema.Remove("NPC", out var npcSchema))
        {
            // Add common components to NPC schema
            IEnumerable<JProperty> thisComps = npcSchema.Children<JProperty>();
            if (commonSchema != null)
                thisComps = thisComps.Concat(commonSchema.Children<JProperty>());

            // var globals = typeof(NPC).GetField("_globals", BindingFlags.Instance | BindingFlags.NonPublic);

            Dictionary<Type, Dictionary<FieldInfo, object>> everything = [];
            // we will check if the listed components exist, if the fields inside exist,
            // and if they're being set to something that isn't the default
            foreach (var kvp in thisComps)
            {
                Type possibleComponent = Mod.Code.GetType($"Terramon.Content.NPCs.NPC{kvp.Name}");
                if (possibleComponent is null)
                    continue;

                everything[possibleComponent] = [];

                bool actuallyDoesStuff = false;
                foreach (var prop in kvp.Value.Children<JProperty>())
                {
                    var fieldInfo = possibleComponent.GetRuntimeField(prop.Name);
                    // field doesn't exist
                    if (fieldInfo is null)
                        continue;
                    // field is being set to the default
                    var setToThis = prop.Value.ToObject(fieldInfo.FieldType);
                    if (setToThis is string s)
                    {
                        if (string.IsNullOrEmpty(s))
                            continue;
                    }
                    else if (setToThis == Activator.CreateInstance(fieldInfo.FieldType))
                        continue;
                    everything[possibleComponent][fieldInfo] = setToThis;
                    // actually does stuff!
                    actuallyDoesStuff = true;
                }
                // don't wanna put it in the IL if nothing is done with it
                // (actually it should be there, but null. so the component is actually enabled)
                if (!actuallyDoesStuff)
                    everything[possibleComponent] = default;
            }
            _tokenized[id] = everything;
        }
        NPCSchemaCache.Add(id, null);
    }

    private void LoadBanner(ushort id, DatabaseV2.PokemonSchema schema)
    {
        // Load banner item
        var banner = new PokeBannerItem(id, schema);
        Mod.AddContent(banner);
        IDToBannerType.Add(id, banner.Type);
        
        ShinyBanners.Add(new PokeBannerItem(id, schema, banner.Type));
    }

    private void LoadShinyBanners()
    {
        // Add shiny banners to mod content
        foreach (var banner in ShinyBanners) Mod.AddContent(banner);

        ShinyBanners = null;
    }

    public static Asset<Texture2D> RequestTexture(IPokemonEntity entity)
    {
        var pathBuilder = new StringBuilder(entity.Texture);
        var data = entity.Data;
        var i = entity.ID - 1;
        if (HasGenderDifference[i])
            if ((data != null ? data.Gender == Gender.Female ? 1 : 0 : 0) != 0)
                pathBuilder.Append('F');
        if (HasPetExclusiveTexture[i] && entity.GetType() == typeof(PokemonPet))
            pathBuilder.Append("_Pet");
        var str = data?.Variant;
        if (!string.IsNullOrEmpty(str))
            pathBuilder.Append('_').Append(data.Variant);
        if (data is { IsShiny: true })
            pathBuilder.Append("_S");
        return ModContent.Request<Texture2D>(pathBuilder.ToString());
    }


    public override void Load()
    {
        IDToNPCType = [];
        IDToPetType = [];
        IDToBannerType = [];
        NPCSchemaCache = [];
        PetSchemaCache = [];
        GlowTextureCache = [];
        ShinyGlowTextureCache = [];
        HighlightTextures = [];
        ShinyBanners = [];
    }

    public override void Unload()
    {
        IDToNPCType = null;
        IDToPetType = null;
        IDToBannerType = null;
        NPCSchemaCache = null;
        PetSchemaCache = null;
        HasGenderDifference = null;
        HasPetExclusiveTexture = null;
        GlowTextureCache = null;
        ShinyGlowTextureCache = null;
        HighlightTextures = null;
        ShinyBanners = null;
    }
}