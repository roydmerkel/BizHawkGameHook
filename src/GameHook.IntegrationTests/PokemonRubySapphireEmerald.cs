﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace GameHook.IntegrationTests
{
    [TestClass]
    public class PokemonRubySapphireEmerald : BaseTest
    {
        [Ignore]
        [TestMethod]
        public async Task Property_OK_DMA_A_Item()
        {
            await Task.CompletedTask;
        }

        // Emerald Tests
        [TestMethod]
        public async Task Emerald_Property_OK_DMA_B_GameTime()
        {
            await Load_GBA_PokemonEmerald();

            var mapper = await GameHookClient.GetMapperAsync();

            mapper.AssertAreEqual("game_time.hours", 0x02024ADA, [0x09], 9);
            mapper.AssertAreEqual("game_time.minutes", 0x02024ADC, [0x16], 22);
            mapper.AssertAreEqual("game_time.seconds", 0x02024ADD, [0x1A], 26);
            mapper.AssertAreEqual("game_time.frames", 0x02024ADE, [0x14], 20);
        }

        [TestMethod]
        public async Task Emerald_Property_OK_PokemonPartyStructure()
        {
            await Load_GBA_PokemonEmerald(1);

            var mapper = await GameHookClient.GetMapperAsync();

            mapper.AssertAreEqual("player.team.0.species", 0x20, [0x18, 0x01], "Torchic");
            mapper.AssertAreEqual("player.team.0.internals.personality_value", [0x28, 0xBA, 0xB6, 0x64], 1689696808);
            mapper.AssertAreEqual("player.team.0.internals.checksum", 0x1C, [0x9B, 0x67], 26523);
            mapper.AssertAreEqual("player.team.0.nickname", 0x08, [0xBD, 0xC2, 0xC3, 0xBD, 0xC5, 0xBF, 0xC8, 0xFF, 0xFF, 0xFF], "CHICKEN");
            mapper.AssertAreEqual("player.team.0.exp", 0x24, [0x5A, 0x06, 0x00, 0x00], 1626);
            mapper.AssertAreEqual("player.team.0.held_item", 0x22, [0x00, 0x00], null);
            mapper.AssertAreEqual("player.team.0.friendship", 0x29, [0x72], 114);
            mapper.AssertAreEqual("player.team.0.misc.is_bad_egg", 0x13, [0x02], false);

            mapper.AssertAreEqual("player.team.0.level", 0x54, [0x0E], 14);
            mapper.AssertAreEqual("player.team.0.stats.hp", 0x56, [0x28, 0x00], 40);

            mapper.AssertAreEqual("player.team.0.moves.0.move", 0x2C, [0x0A, 0x00], "Scratch");
            mapper.AssertAreEqual("player.team.0.moves.0.pp", 0x34, [0x23], 35);
            mapper.AssertAreEqual("player.team.0.moves.0.pp_up", 0x5B, [0x00], 0);

            mapper.AssertAreEqual("player.team.0.ivs.hp", 0x48, [0x3B, 0x4B, 0x3B, 0x1E], 27);
            mapper.AssertAreEqual("player.team.0.ivs.attack", 0x48, [0x3B, 0x4B, 0x3B, 0x1E], 25);
            mapper.AssertAreEqual("player.team.0.ivs.defense", 0x48, [0x3B, 0x4B, 0x3B, 0x1E], 18);
            mapper.AssertAreEqual("player.team.0.ivs.speed", 0x48, [0x3B, 0x4B, 0x3B, 0x1E], 22);
            mapper.AssertAreEqual("player.team.0.ivs.special_attack", 0x48, [0x3B, 0x4B, 0x3B, 0x1E], 3);
            mapper.AssertAreEqual("player.team.0.ivs.special_defense", 0x48, [0x3B, 0x4B, 0x3B, 0x1E], 15);

            mapper.AssertAreEqual("player.team.0.evs.hp", 0x38, [0x08], 8);
            mapper.AssertAreEqual("player.team.0.evs.attack", 0x39, [0x06], 6);
            mapper.AssertAreEqual("player.team.0.evs.defense", 0x3A, [0x06], 6);
            mapper.AssertAreEqual("player.team.0.evs.speed", 0x3B, [0x0A], 10);
            mapper.AssertAreEqual("player.team.0.evs.special_attack", 0x3C, [0x00], 0);
            mapper.AssertAreEqual("player.team.0.evs.special_defense", 0x3D, [0x01], 1);


            mapper.AssertAreEqual("player.team.1.species", 0x20, [0x27, 0x01], "Lotad");
            mapper.AssertAreEqual("player.team.1.internals.personality_value", [0xA5, 0x04, 0xBB, 0x1F], 532350117);
            mapper.AssertAreEqual("player.team.1.internals.checksum", 0x1C, [0xDC, 0x80], 32988);
            mapper.AssertAreEqual("player.team.1.nickname", 0x08, [0xCA, 0xC3, 0xD4, 0xD4, 0xBB, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF], "PIZZA");
            mapper.AssertAreEqual("player.team.1.exp", 0x24, [0x95, 0x00, 0x00, 0x00], 149);
            mapper.AssertAreEqual("player.team.1.held_item", 0x22, [0x00, 0x00], null);
            mapper.AssertAreEqual("player.team.1.friendship", 0x29, [0x53], 83);
            mapper.AssertAreEqual("player.team.1.misc.is_bad_egg", 0x13, [0x02], false);

            mapper.AssertAreEqual("player.team.1.level", 0x54, [0x05], 5);
            mapper.AssertAreEqual("player.team.1.stats.hp", 0x56, [0x13, 0x00], 19);

            mapper.AssertAreEqual("player.team.1.moves.0.move", 0x2C, [0x36, 0x01], "Astonish");
            mapper.AssertAreEqual("player.team.1.moves.0.pp", 0x34, [0x0F], 15);
            mapper.AssertAreEqual("player.team.1.moves.0.pp_up", 0x5B, [0x00], 0);

            mapper.AssertAreEqual("player.team.1.ivs.hp", 0x48, [0xB3, 0x32, 0x77, 0x9C], 19);
            mapper.AssertAreEqual("player.team.1.ivs.attack", 0x48, [0xB3, 0x32, 0x77, 0x9C], 21);
            mapper.AssertAreEqual("player.team.1.ivs.defense", 0x48, [0xB3, 0x32, 0x77, 0x9C], 12);
            mapper.AssertAreEqual("player.team.1.ivs.speed", 0x48, [0xB3, 0x32, 0x77, 0x9C], 14);
            mapper.AssertAreEqual("player.team.1.ivs.special_attack", 0x48, [0xB3, 0x32, 0x77, 0x9C], 7);
            mapper.AssertAreEqual("player.team.1.ivs.special_defense", 0x48, [0xB3, 0x32, 0x77, 0x9C], 14);

            mapper.AssertAreEqual("player.team.1.evs.hp", 0x38, [0x00], 0);
            mapper.AssertAreEqual("player.team.1.evs.attack", 0x39, [0x00], 0);
            mapper.AssertAreEqual("player.team.1.evs.defense", 0x3A, [0x01], 1);
            mapper.AssertAreEqual("player.team.1.evs.speed", 0x3B, [0x00], 0);
            mapper.AssertAreEqual("player.team.1.evs.special_attack", 0x3C, [0x00], 0);
            mapper.AssertAreEqual("player.team.1.evs.special_defense", 0x3D, [0x01], 1);
        }
        [TestMethod]
        public async Task Emerald_Property_OK_MetaProperties_Index0()
        {
            await Load_GBA_PokemonEmerald(1);

            var mapper = await GameHookClient.GetMapperAsync();

            mapper.AssertAreEqual("meta.generation", 3);
            mapper.AssertAreEqual("meta.game_name", "Emerald");
            mapper.AssertAreEqual("meta.game_type", "Third Version");
        }
        [TestMethod]
        public async Task Emerald_Property_OK_GameTime()
        {
            await Load_GBA_PokemonEmerald(1);

            var mapper = await GameHookClient.GetMapperAsync();

            mapper.AssertAreEqual("game_time.hours", 0x02024AC2, [0x07], 7);
            mapper.AssertAreEqual("game_time.minutes", 0x02024AC4, [0x04], 4);
            mapper.AssertAreEqual("game_time.seconds", 0x02024AC5, [0x30], 48);
            mapper.AssertAreEqual("game_time.frames", 0x02024AC6, [0x34],52);
        }
        [TestMethod]
        public async Task Emerald_Property_OK_Options()
        {
            await Load_GBA_PokemonEmerald(1);

            var mapper = await GameHookClient.GetMapperAsync();

            mapper.AssertAreEqual("options.text_speed", 0x02024AC8, [0x02], 2);
            mapper.AssertAreEqual("options.battle_animations", 0x02024AC9, [0x06], true);
            mapper.AssertAreEqual("options.battle_style", 0x02024AC9, [0x06], true);
            mapper.AssertAreEqual("options.sound", 0x02024AC9, [0x06], false);
            mapper.AssertAreEqual("options.button_mode", 0x02024AC7, [0x00], 0);
        }
        [TestMethod]
        public async Task Emerald_Property_OK_Overworld_Index0()
        {
            await Load_GBA_PokemonEmerald(1);

            var mapper = await GameHookClient.GetMapperAsync();

            mapper.AssertAreEqual("overworld.map_name", 0x020322E4, [0x0B, 0x03], "RUSTBORO_CITY - GYM");
        }
        [TestMethod]
        public async Task Emerald_Property_OK_PointersCallbacks()
        {
            await Load_GBA_PokemonEmerald(1);

            var mapper = await GameHookClient.GetMapperAsync();

            mapper.AssertAreEqual("pointers.dma_1", 0x03005D8C, [0x60, 0x5A, 0x02, 0x02], 33708640);
            mapper.AssertAreEqual("pointers.dma_2", 0x03005D90, [0xB4, 0x4A, 0x02, 0x02], 33704628);
            mapper.AssertAreEqual("pointers.dma_3", 0x03005D94, [0x68, 0x98, 0x02, 0x02], 33724520);
            mapper.AssertAreEqual("pointers.callback_1", 0x030022C0, [0x05, 0x5E, 0x08, 0x08], "Overworld");
            mapper.AssertAreEqual("pointers.callback_2", 0x030022C4, [0x5D, 0x5E, 0x08, 0x08], "Overworld");
        }
        [TestMethod]
        public async Task Emerald_Property_OK_Player()
        {
            await Load_GBA_PokemonEmerald(1);

            var mapper = await GameHookClient.GetMapperAsync();

            mapper.AssertAreEqual("player.name", 0x02024AB4, [0xBB, 0xC1, 0xC2, 0xC4, 0xFF, 0xFF, 0xFF, 0xFF], "AGHJ");
            mapper.AssertAreEqual("player.gender", 0x02024ABC, [0x00], "Male");
            mapper.AssertAreEqual("player.player_id", 0x02024ABE, [0xD7, 0x68], 26839);
            mapper.AssertAreEqual("player.badges.0", 0x02026D6E, [0x00], false);
        }
        [TestMethod]
        public async Task Emerald_Property_OK_PlayerBagStructure()
        {
            await Load_GBA_PokemonEmerald(1);

            var mapper = await GameHookClient.GetMapperAsync();

            mapper.AssertAreEqual("bag.quantity_decryption_key", 0x02024B60, [0x29, 0x40], 16425);
            mapper.AssertAreEqual("bag.money", 0x02025EF0, [0x39, 0x54], 5136);
            mapper.AssertAreEqual("bag.items.0.item", 0x02025FC0, [0x0E, 0x00], "Antidote");
            mapper.AssertAreEqual("bag.items.0.quantity", 0x02025FC2, [0x2B, 0x40], 2);
            mapper.AssertAreEqual("bag.items.1.item", 0x02025FC4, [0x12, 0x00], "Paralyze Heal");
            mapper.AssertAreEqual("bag.items.1.quantity", 0x02025FC6, [0x2A, 0x40], 3);
        }
        [TestMethod]
        public async Task Emerald_Property_OK_BattleStructure()
        {
            await Load_GBA_PokemonEmerald(1);

            var mapper = await GameHookClient.GetMapperAsync();

            mapper.AssertAreEqual("battle.outcome", null);
            mapper.AssertAreEqual("battle.battle_flags.trainer", 0x02022FEC, [0x0D, 0x00, 0x00, 0x00], true);
            mapper.AssertAreEqual("battle.battle_flags.is_battle", 0x02022FEC, [0x0D, 0x00, 0x00, 0x00], true);
            mapper.AssertAreEqual("battle.field.weather", 0x020243CC, [0x00], null);
            mapper.AssertAreEqual("battle.field.weather_count", 0x020243F8, [0x00], 0);
            mapper.AssertAreEqual("battle.player.team_count", 0x020244E9, [0x03], 3);
            mapper.AssertAreEqual("battle.player.active_pokemon.species", 0x02024084, [0x18, 0x01], "Torchic");
            mapper.AssertAreEqual("battle.player.active_pokemon.ability", 0x020240A4, [0x42], "Blaze");
            mapper.AssertAreEqual("battle.player.active_pokemon.type_1", 0x020240A5, [0x0A], "Fire");

            mapper.AssertAreEqual("battle.player.active_pokemon.stats.hp", 0x020240AC, [0x18, 0x00], 24);
            mapper.AssertAreEqual("battle.player.active_pokemon.stats.attack", 0x02024086, [0x19, 0x00], 25);
            mapper.AssertAreEqual("battle.player.active_pokemon.stats.defense", 0x02024088, [0x13, 0x00], 19);
            mapper.AssertAreEqual("battle.player.active_pokemon.stats.speed", 0x0202408A, [0x14, 0x00], 20);
            mapper.AssertAreEqual("battle.player.active_pokemon.stats.special_attack", 0x0202408C, [0x16, 0x00], 22);
            mapper.AssertAreEqual("battle.player.active_pokemon.stats.special_defense", 0x0202408E, [0x15, 0x00], 21);

            mapper.AssertAreEqual("battle.player.active_pokemon.moves.0.move", 0x02024090, [0x0A, 0x00], "Scratch");
            mapper.AssertAreEqual("battle.player.active_pokemon.ivs.hp", 0x02024098, [0x3B, 0x4B, 0x3B, 0x1E], 27);
            mapper.AssertAreEqual("battle.player.active_pokemon.modifiers.attack", 0x0202409D, [0x06], 0);

            mapper.AssertAreEqual("battle.player.team.0.internals.personality_value", [0x28, 0xBA, 0xB6, 0x64], 1689696808);

            mapper.AssertAreEqual("battle.opponent.trainer", 0x02038BCA, [0xE3, 0x01], "TWINS_GINA_AND_MIA_1");
            mapper.AssertAreEqual("battle.opponent.id", 0x02038BCA, [0xE3, 0x01], 483);
            mapper.AssertAreEqual("battle.opponent.active_pokemon.species", 0x020240DC, [0x2A, 0x01], "Seedot");
        }
        //RUBY SAPPHIRE MAPPER UPDATE REQUIRED BEFORE THESE TESTS WILL WORK
        // [TestMethod]
        // public async Task Ruby_Property_OK_Meta()
        // {
        //     await Load_GBA_PokemonRuby();
        // 
        //     var mapper = await GameHookClient.GetMapperAsync();
        // 
        //     mapper.AssertAreEqual("meta.generation", 3);
        //     mapper.AssertAreEqual("meta.gameName", "Ruby and Sapphire");
        //     mapper.AssertAreEqual("meta.gameType", "Original");
        // }
        // [TestMethod]
        // public async Task Ruby_Property_OK_Pointers()
        // {
        //     await Load_GBA_PokemonRuby();
        // 
        //     var mapper = await GameHookClient.GetMapperAsync();
        // 
        //     mapper.AssertAreEqual("pointers.callback_1", 0x03001770, [0x25, 0x08, 0x01, 0x08], "Battle");
        // }
        // [TestMethod]
        // public async Task Ruby_Property_OK_GameTime()
        // {
        //     await Load_GBA_PokemonRuby();
        // 
        //     var mapper = await GameHookClient.GetMapperAsync();
        // 
        //     mapper.AssertAreEqual("gameTime.hours", 0x02024EB2, [0x00], 0);
        //     mapper.AssertAreEqual("gameTime.minutes", 0x02024EB4, [0x1C], 28);
        //     mapper.AssertAreEqual("gameTime.seconds", 0x02024EB5, [0x3A], 58);
        //     mapper.AssertAreEqual("gameTime.frames", 0x02024EB6, [0x10], 16);
        // }
        // [TestMethod]
        // public async Task Ruby_Property_OK_Overworld()
        // {
        //     await Load_GBA_PokemonRuby();
        // 
        //     var mapper = await GameHookClient.GetMapperAsync();
        // 
        //     mapper.AssertAreEqual("overworld.map_name", 0x02025738, [0x0B, 0x03], "RUSTBORO_CITY - GYM");
        // }
        // [TestMethod]
        // public async Task Ruby_Property_OK_Player()
        // {
        //     await Load_GBA_PokemonRuby();
        // 
        //     var mapper = await GameHookClient.GetMapperAsync();
        // 
        //     mapper.AssertAreEqual("player.playerId", 0x020228D6, [0x10, 0x50], 20496);
        //     mapper.AssertAreEqual("player.name", 0x02024EA4, [0xBC, 0xC9, 0xBC, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF], "BOB");
        //     mapper.AssertAreEqual("player.gender", 0x02024EAC, [0x00], "Male");
        //     mapper.AssertAreEqual("player.teamCount", 0x03004350, [0x01], 1);
        //     mapper.AssertAreEqual("player.badges.badge1", 0x02026A15, [0x00], false);
        //     mapper.AssertAreEqual("player.badges.badge2", 0x02026A15, [0x00], false);
        //     mapper.AssertAreEqual("player.badges.badge3", 0x02026A15, [0x00], false);
        //     mapper.AssertAreEqual("player.badges.badge4", 0x02026A15, [0x00], false);
        //     mapper.AssertAreEqual("player.badges.badge5", 0x02026A15, [0x00], false);
        //     mapper.AssertAreEqual("player.badges.badge6", 0x02026A15, [0x00], false);
        //     mapper.AssertAreEqual("player.badges.badge7", 0x02026A15, [0x00], false);
        //     mapper.AssertAreEqual("player.badges.badge8", 0x02026A15, [0x00], false);
        // }
        // [TestMethod]
        // public async Task Ruby_Property_OK_BattleStructure()
        // {
        //     await Load_GBA_PokemonRuby();
        // 
        //     var mapper = await GameHookClient.GetMapperAsync();
        // 
        //     mapper.AssertAreEqual("battle.outcome", 0x02024D26, [0x00], null);
        //     mapper.AssertAreEqual("battle.other.type.trainer", 0x020239F8, [0x0C, 0x00, 0x00, 0x00], true);
        //     mapper.AssertAreEqual("battle.other.type.double", 0x020239F8, [0x0C, 0x00, 0x00, 0x00], false);
        // 
        //     mapper.AssertAreEqual("battle.field.weather", 0x02024DB8, [0x00], null);
        //     mapper.AssertAreEqual("battle.field.weatherCount", 0x02024DE4, [0x00], 0);
        // 
        //     mapper.AssertAreEqual("battle.player.activePokemon.species", 0x02024A80, [0x1B, 0x01], "Mudkip");
        //     mapper.AssertAreEqual("battle.opponent.activePokemon.species", 0x02024AD8, [0x40, 0x01], "Nosepass");
        // }
    }
}
