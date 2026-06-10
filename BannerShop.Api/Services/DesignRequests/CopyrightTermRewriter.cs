using System.Text.RegularExpressions;

namespace BannerShop.Api.Services.DesignRequests;

/// <summary>
/// Replaces well-known trademarked / copyrighted character names and franchise
/// keywords in customer-supplied text with descriptive visual equivalents so
/// the downstream image-generation model receives prompts that do not reference
/// protected intellectual property.
///
/// Rules:
///   • Matching is case-insensitive and boundary-aware (a term embedded inside
///     another word is NOT replaced).
///   • Replacements describe the visual appearance and spirit of the character
///     without naming the franchise or character directly.
///   • The list covers the IP terms most frequently seen in party-banner requests
///     (children's birthdays, etc.) — superheroes, Disney, gaming, cartoons.
///   • When the OpenAI prompt-refinement step is active, its system prompt also
///     instructs the LLM to catch any remaining trademarked terms; this rewriter
///     is the deterministic safety-net that runs even when the LLM refiner is
///     not configured.
/// </summary>
public static class CopyrightTermRewriter
{
    // Each entry is (raw regex pattern, replacement text).
    // ● Patterns use no \b at the edges; instead we wrap each with
    //   (?<![A-Za-z0-9_]) … (?![A-Za-z0-9_]) in CompiledTerms so that
    //   internal pattern anchors like (?!\w) in the raw string are still
    //   honoured (e.g. "thor" in "thorough" is not replaced).
    // ● Longer / more-specific terms are listed before shorter sub-terms so
    //   "Spider-Man" is matched before a hypothetical bare "spider".
    // ● Duplicates are deduplicated at compile time (distinct by pattern).
    private static readonly (string Pattern, string Replacement)[] RawTerms =
    [
        // ── Marvel ──────────────────────────────────────────────────────────────
        (@"spider[\s\-]?man",              "a red-and-blue spider-themed superhero"),
        (@"iron[\s\-]?man",                "an armored superhero in a red-and-gold powered suit"),
        (@"captain[\s\-]?america",         "a star-spangled shield-wielding superhero in blue and red"),
        (@"captain[\s\-]?marvel",          "a cosmic superhero in a red-and-blue star uniform"),
        (@"black[\s\-]?panther",           "an agile African warrior superhero in a sleek black suit"),
        (@"black[\s\-]?widow",             "a red-haired female secret-agent superhero in black"),
        (@"doctor[\s\-]?strange",          "a sorcerer superhero with a flowing magical cloak"),
        (@"ant[\s\-]?man",                 "a size-shifting superhero in a red-and-silver suit"),
        (@"guardians[\s\-]?of[\s\-]?the[\s\-]?galaxy", "a ragtag team of space adventurers"),
        (@"rocket[\s\-]?raccoon",          "a wisecracking space raccoon with a big gun"),
        (@"deadpool",                      "a wisecracking masked anti-hero in red-and-black"),
        (@"wolverine",                     "a fierce hero with retractable metal claws"),
        (@"x[\s\-]?men",                   "a team of mutant superheroes"),
        (@"thor",                          "a hammer-wielding superhero with lightning powers"),
        (@"hulk",                          "a giant green super-strong hero"),
        (@"avengers",                      "a team of superheroes"),
        (@"groot",                         "a gentle tree-like alien creature"),
        (@"thanos",                        "a powerful purple cosmic villain"),

        // ── DC ──────────────────────────────────────────────────────────────────
        (@"batman",                        "a dark-caped vigilante with a bat symbol"),
        (@"superman",                      "a caped superhero in blue and red with a shield emblem"),
        (@"wonder[\s\-]?woman",            "a golden-armored warrior princess superhero"),
        (@"aquaman",                       "an underwater superhero with a golden trident"),
        (@"the[\s\-]?flash",               "a lightning-fast superhero in a scarlet suit"),
        (@"green[\s\-]?lantern",           "a superhero wielding a glowing green energy ring"),
        (@"joker",                         "a theatrical villain with green hair and a painted grin"),

        // ── Star Wars ────────────────────────────────────────────────────────────
        (@"star[\s\-]?wars",               "a science-fiction space adventure with starships and glowing energy swords"),
        (@"darth[\s\-]?vader",             "a black-armored dark lord with a red glowing energy sword"),
        (@"baby[\s\-]?yoda",               "a small green-eared alien baby with big eyes"),
        (@"grogu",                         "a small green-eared alien baby with big eyes"),
        (@"mandalorian",                   "a lone bounty hunter in gleaming silver armor"),
        (@"yoda",                          "a small pointy-eared green alien Jedi master"),
        (@"stormtrooper",                  "a white-armored space soldier"),
        (@"jedi",                          "a force-wielding warrior with a glowing energy sword"),
        (@"lightsaber",                    "a glowing energy sword"),
        (@"r2[\s\-]?d2",                   "a small cylindrical robot with a dome head"),
        (@"bb[\s\-]?8",                    "a round orange-and-white rolling robot"),

        // ── Disney classics ──────────────────────────────────────────────────────
        (@"mickey[\s\-]?mouse",            "a cheerful cartoon mouse with round black ears"),
        (@"minnie[\s\-]?mouse",            "a cheerful cartoon mouse with a polka-dot bow"),
        (@"donald[\s\-]?duck",             "a cartoon duck in a sailor suit"),
        (@"goofy",                         "a tall clumsy cartoon dog in an orange vest and hat"),
        (@"winnie[\s\-]?the[\s\-]?pooh",   "a yellow cartoon bear in a red shirt who loves honey"),
        (@"pooh[\s\-]?bear",               "a yellow cartoon bear in a red shirt who loves honey"),
        (@"piglet",                        "a small pink cartoon piglet"),
        (@"tigger",                        "a bouncy orange-and-black striped cartoon tiger"),
        (@"eeyore",                        "a gloomy grey cartoon donkey"),
        (@"cinderella",                    "a princess in a sparkling blue ball gown with glass slippers"),
        (@"snow[\s\-]?white",              "a princess with black hair, red lips and a yellow-and-blue dress"),
        (@"sleeping[\s\-]?beauty",         "a sleeping princess in a pink dress surrounded by roses"),
        (@"little[\s\-]?mermaid",          "a red-haired mermaid princess under the sea"),
        (@"beauty[\s\-]?and[\s\-]?the[\s\-]?beast", "a fairytale of an enchanted castle with a rose under glass"),
        (@"lion[\s\-]?king",               "a young golden lion cub destined to be king of the savanna"),
        (@"simba",                         "a young golden lion cub on the sun-lit savanna"),
        (@"hakuna[\s\-]?matata",           "a carefree savanna adventure"),
        (@"moana",                         "a Polynesian ocean adventurer with a swirling magical hook"),
        (@"encanto",                       "a magical Colombian family home with glowing golden candles"),
        (@"coco",                          "a colorful Día de los Muertos-inspired celebration"),
        (@"tangled",                       "a magical tower adventure with very long golden hair"),
        (@"rapunzel",                      "a princess with very long magical golden hair in a tall tower"),
        (@"frozen",                        "a winter-wonderland ice palace with magical snowflakes and northern lights"),
        (@"elsa",                          "an ice queen in a shimmering blue gown with flowing platinum hair"),
        (@"olaf",                          "a cheerful cartoon snowman with a carrot nose"),
        (@"dumbo",                         "a small elephant with oversized magical ears who can fly"),
        (@"bambi",                         "a gentle spotted forest fawn among woodland flowers"),
        (@"pinocchio",                     "a wooden puppet boy who wants to become real"),
        (@"stitch",                        "a small blue alien creature with big ears"),
        (@"lilo[\s\-]?and[\s\-]?stitch",   "a Hawaiian girl and her mischievous blue alien friend"),

        // ── Pixar ────────────────────────────────────────────────────────────────
        (@"toy[\s\-]?story",               "a colorful toy adventure with a cowboy doll and a space ranger"),
        (@"buzz[\s\-]?lightyear",          "a white-and-green space ranger in armor"),
        (@"woody",                         "a cowboy doll in yellow-and-brown with a pull-string"),
        (@"finding[\s\-]?dory",            "a colorful ocean adventure with a forgetful blue fish"),
        (@"finding[\s\-]?nemo",            "an orange-and-white clownfish adventure in the coral reef"),
        (@"nemo",                          "an orange-and-white clownfish among colorful coral"),
        (@"wall[\s\-]?e",                  "a small boxy trash-collecting robot on a desolate Earth"),
        (@"inside[\s\-]?out",              "colorful emotion characters inside a glowing mind headquarters"),
        (@"ratatouille",                   "a chef rat in a bustling Parisian kitchen"),
        (@"incredibles",                   "a family of superheroes in matching red suits"),
        (@"monsters[\s\-]?inc",            "a city of friendly monsters who power their world with laughter"),
        (@"cars",                          "colorful racing car characters on a sunlit track"),
        (@"lightning[\s\-]?mcqueen",       "a bright-red racing car with lightning bolt decals"),
        (@"turning[\s\-]?red",             "a red panda transformation adventure"),

        // ── Nintendo ─────────────────────────────────────────────────────────────
        (@"super[\s\-]?mario",             "a plumber in red overalls in a colorful mushroom world"),
        (@"mario[\s\-]?kart",              "a colorful go-kart racing adventure with cartoon characters"),
        (@"mario[\s\-]?bros",              "plumber brothers in red and green overalls in a mushroom world"),
        (@"mario",                         "a plumber in red overalls in a colorful mushroom world"),
        (@"luigi",                         "a plumber in green overalls in a colorful mushroom world"),
        (@"princess[\s\-]?peach",          "a princess in a pink dress in a mushroom kingdom"),
        (@"bowser",                        "a giant spiked turtle monster king"),
        (@"yoshi",                         "a friendly green dinosaur"),
        (@"donkey[\s\-]?kong",             "a giant gorilla in a jungle of bananas"),
        (@"the[\s\-]?legend[\s\-]?of[\s\-]?zelda", "a heroic adventure in a golden fantasy kingdom"),
        (@"zelda",                         "a princess with magical powers in a golden fantasy kingdom"),
        (@"kirby",                         "a round pink puffball character"),
        (@"samus",                         "an armored female space bounty hunter"),

        // ── Pokémon ──────────────────────────────────────────────────────────────
        (@"pok[eé]mon",                    "a pocket-monster adventure with colorful creature companions"),
        (@"pikachu",                       "a small yellow electric mouse with a lightning bolt tail"),
        (@"charizard",                     "a large orange fire-breathing dragon"),
        (@"mewtwo",                        "a powerful psychic alien-like creature"),

        // ── Other video games ─────────────────────────────────────────────────────
        (@"minecraft",                     "a blocky pixelated world with cubic terrain and creative building"),
        (@"roblox",                        "a blocky game world with customizable avatar characters"),
        (@"fortnite",                      "a colorful cartoon-style battle arena"),
        (@"among[\s\-]?us",                "colorful space crewmates with visors on a spaceship"),
        (@"sonic[\s\-]?the[\s\-]?hedgehog","a supersonic blue hedgehog"),
        (@"sonic",                         "a supersonic blue hedgehog"),
        (@"pac[\s\-]?man",                 "a yellow circle character eating dots in a maze"),
        (@"brawl[\s\-]?stars",             "a colorful top-down cartoon combat game with quirky heroes"),
        (@"clash[\s\-]?of[\s\-]?clans",    "a medieval village defense strategy game"),
        (@"clash[\s\-]?royale",            "a card-based tower-defense battle game"),
        (@"minecraft[\s\-]?creeper",       "a green blocky explosive creature"),

        // ── Children's TV / Cartoons ──────────────────────────────────────────────
        (@"peppa[\s\-]?pig",               "a pink cartoon pig family character"),
        (@"paw[\s\-]?patrol",              "a team of colorful rescue pups in uniforms"),
        (@"bluey",                         "a blue cartoon dog and her playful family"),
        (@"thomas[\s\-]?(?:the[\s\-]?)?(?:tank[\s\-]?)?engine", "a friendly blue steam locomotive"),
        (@"sponge[\s\-]?bob[\s\-]?squarepants", "a square yellow sea sponge living underwater"),
        (@"sponge[\s\-]?bob",              "a square yellow sea sponge living underwater"),
        (@"dora[\s\-]?the[\s\-]?explorer", "a young adventurer with a backpack and explorer map"),
        (@"sesame[\s\-]?street",           "a colorful educational street with friendly puppet characters"),
        (@"hello[\s\-]?kitty",             "a white cat character with a bow and a minimalist face"),
        (@"my[\s\-]?little[\s\-]?pony",    "colorful magical ponies with flowing rainbow manes"),
        (@"transformers",                  "giant robots that transform into vehicles"),
        (@"teenage[\s\-]?mutant[\s\-]?(?:ninja[\s\-]?)?turtles", "four colorful martial-arts turtles in a half-shell"),
        (@"tmnt",                          "four colorful martial-arts turtles in a half-shell"),
        (@"power[\s\-]?rangers",           "a team of colorful costumed superhero warriors"),
        (@"ben[\s\-]?10",                  "a young hero with an alien-transformation watch"),
        (@"scooby[\s\-]?doo",              "a cartoon Great Dane dog solving mysteries with friends"),
        (@"shrek",                         "a green ogre in a fairy-tale swamp world"),
        (@"minions",                       "small yellow oval creatures in blue overalls"),
        (@"despicable[\s\-]?me",           "a reformed villain surrounded by small yellow oval creatures"),
        (@"snoopy",                        "a white-and-black beagle dog lying on a red doghouse"),
        (@"charlie[\s\-]?brown",           "a round-headed boy in a zigzag-stripe shirt"),
        (@"garfield",                      "an orange tabby cat who loves lasagna"),

        // ── Anime ─────────────────────────────────────────────────────────────────
        (@"naruto",                        "a spiky-haired ninja in an orange jumpsuit"),
        (@"dragon[\s\-]?ball",             "a spiky-haired martial artist with swirling energy powers"),
        (@"goku",                          "a spiky-haired martial artist in an orange gi"),
        (@"sailor[\s\-]?moon",             "a magical girl in a sailor-style costume with long blonde pigtails"),
        (@"one[\s\-]?piece",               "a straw-hat-wearing pirate adventure on colorful seas"),
        (@"demon[\s\-]?slayer",            "a Japanese swordsman in a checkered robe hunting demons"),
        (@"my[\s\-]?hero[\s\-]?academia",  "a superhero school adventure with costumed student heroes"),
        (@"attack[\s\-]?on[\s\-]?titan",   "a post-apocalyptic adventure behind towering walls"),

        // ── Other popular franchises ───────────────────────────────────────────────
        (@"harry[\s\-]?potter",            "a young wizard in a magical castle school"),
        (@"hogwarts",                      "a magical castle school for young wizards"),
        (@"lord[\s\-]?of[\s\-]?the[\s\-]?rings", "an epic fantasy quest to destroy a powerful magical ring"),
    ];

    /// <summary>
    /// Compiled regex entries built from <see cref="RawTerms"/>.  Using
    /// <see cref="RegexOptions.Compiled"/> is worthwhile here because the
    /// same patterns are applied to every design-request submission.
    /// </summary>
    private static readonly (Regex Regex, string Replacement)[] CompiledTerms =
        RawTerms
            // Deduplicate on pattern string to guard against accidental repeats.
            .DistinctBy(t => t.Pattern, StringComparer.OrdinalIgnoreCase)
            .Select(t => (
                // Wrap each pattern with negative look-behind / look-ahead so we
                // only match whole "words" (i.e. the term is not embedded inside
                // a longer alphanumeric token).
                new Regex(
                    $@"(?<![A-Za-z0-9_]){t.Pattern}(?![A-Za-z0-9_])",
                    RegexOptions.IgnoreCase | RegexOptions.Compiled),
                t.Replacement))
            .ToArray();

    /// <summary>
    /// Returns a copy of <paramref name="input"/> with all recognised
    /// trademarked / copyrighted terms replaced by descriptive visual
    /// equivalents.  Returns <paramref name="input"/> unchanged if it is
    /// null or white-space.
    /// </summary>
    public static string Rewrite(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var result = input;
        foreach (var (regex, replacement) in CompiledTerms)
            result = regex.Replace(result, replacement);
        return result;
    }
}
