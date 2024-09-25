using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using ShopAPI;

namespace ShopRespawn
{
    public class ShopRespawn : BasePlugin
    {
        public override string ModuleName => "[SHOP] Respawn";
        public override string ModuleDescription => "";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "v1.0.1";

        private IShopApi? SHOP_API;
        private const string CategoryName = "Respawn";
        public static JObject? JsonRespawn { get; private set; }
        private readonly PlayerRespawn[] playerRespawns = new PlayerRespawn[65];

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            SHOP_API = IShopApi.Capability.Get();
            if (SHOP_API == null) return;

            LoadConfig();
            InitializeShopItems();
            SetupTimersAndListeners();
        }

        private void LoadConfig()
        {
            string configPath = Path.Combine(ModuleDirectory, "../../configs/plugins/Shop/Respawn.json");
            if (File.Exists(configPath))
            {
                JsonRespawn = JObject.Parse(File.ReadAllText(configPath));
            }
        }

        private void InitializeShopItems()
        {
            if (JsonRespawn == null || SHOP_API == null) return;

            SHOP_API.CreateCategory(CategoryName, "Респавн");

            foreach (var item in JsonRespawn.Properties().Where(p => p.Value is JObject))
            {
                Task.Run(async () =>
                {
                    int itemId = await SHOP_API.AddItem(
                        item.Name,
                        (string)item.Value["name"]!,
                        CategoryName,
                        (int)item.Value["price"]!,
                        (int)item.Value["sellprice"]!,
                        (int)item.Value["duration"]!
                    );
                    SHOP_API.SetItemCallbacks(itemId, OnClientBuyItem, OnClientSellItem, OnClientToggleItem);
                }).Wait();
            }
        }

        private void SetupTimersAndListeners()
        {
            RegisterListener<Listeners.OnClientDisconnect>(playerSlot => playerRespawns[playerSlot] = null!);
            RegisterListener<Listeners.OnClientConnected>(slot =>
            {
                playerRespawns[slot] = new PlayerRespawn(0, 0);
            });

            RegisterEventHandler<EventRoundStart>((@event, info) =>
            {
                for (var i = 0; i < playerRespawns.Length; i++)
                {
                    if (playerRespawns[i] != null)
                    {
                        playerRespawns[i].UsedRespawns = 0;
                    }
                }

                return HookResult.Continue;
            });

            AddCommand("css_respawn", "", OnCmdRespawn);
        }

        public HookResult OnClientBuyItem(CCSPlayerController player, int itemId, string categoryName, string uniqueName, int buyPrice, int sellPrice, int duration, int count)
        {
            if (TryGetRespawnCount(uniqueName, out int RespawnCount))
            {
                playerRespawns[player.Slot].RespawnCount = RespawnCount;
                playerRespawns[player.Slot].ItemId = itemId;
            }
            else
            {
                Logger.LogError($"{uniqueName} has invalid or missing 'respawncount' in config!");
            }

            return HookResult.Continue;
        }

        public HookResult OnClientToggleItem(CCSPlayerController player, int itemId, string uniqueName, int state)
        {
            if (state == 1 && TryGetRespawnCount(uniqueName, out int RespawnCount))
            {
                playerRespawns[player.Slot].RespawnCount = RespawnCount;
                playerRespawns[player.Slot].ItemId = itemId;
            }
            else if (state == 0)
            {
                OnClientSellItem(player, itemId, uniqueName, 0);
            }

            return HookResult.Continue;
        }

        public HookResult OnClientSellItem(CCSPlayerController player, int itemId, string uniqueName, int sellPrice)
        {
            playerRespawns[player.Slot].RespawnCount = 0;
            playerRespawns[player.Slot].ItemId = 0;

            return HookResult.Continue;
        }

        private void OnCmdRespawn(CCSPlayerController? player, CommandInfo info)
        {
            if (player == null) return;
            Respawn(player);
        }

        private static bool TryGetRespawnCount(string uniqueName, out int RespawnCount)
        {
            RespawnCount = 0;
            if (JsonRespawn != null && JsonRespawn.TryGetValue(uniqueName, out var obj) && obj is JObject jsonItem && jsonItem["respawncount"] != null)
            {
                RespawnCount = (int)jsonItem["respawncount"]!;
                return true;
            }
            return false;
        }

        private void Respawn(CCSPlayerController player)
        {
            var playerRespawn = playerRespawns[player.Slot];

            if (playerRespawn.RespawnCount <= playerRespawn.UsedRespawns)
            {
                player.PrintToChat(Localizer["respawn.Limit"]);
                return;
            }

            if (player.TeamNum is (int)CsTeam.None or (int)CsTeam.Spectator)
            {
                player.PrintToChat(Localizer["respawn.InTeam"]);
                return;
            }

            if (player.PawnIsAlive)
            {
                player.PrintToChat(Localizer["respawn.IsAlive"]);
                return;
            }

            var playerPawn = player.PlayerPawn.Value;

            if (playerPawn == null) return;

            VirtualFunctions.CBasePlayerController_SetPawnFunc.Invoke(player, playerPawn, true, false);
            VirtualFunction.CreateVoid<CCSPlayerController>(player.Handle, GameData.GetOffset("CCSPlayerController_Respawn"))(player);
            playerRespawns[player.Slot].UsedRespawns++;
            player.PrintToChat(Localizer["respawn.Success"]);
        }

        public record class PlayerRespawn(int RespawnCount, int ItemId)
        {
            public int RespawnCount { get; set; } = RespawnCount;
            public int ItemId { get; set; } = ItemId;
            public int UsedRespawns { get; set; }
        };
    }
}