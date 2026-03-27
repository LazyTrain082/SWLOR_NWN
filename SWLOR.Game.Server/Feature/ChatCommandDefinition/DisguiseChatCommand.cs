using System.Collections.Generic;
using System.Linq;
using SWLOR.Game.Server.Enumeration;
using SWLOR.Game.Server.Service;
using SWLOR.Game.Server.Service.ChatCommandService;

namespace SWLOR.Game.Server.Feature.ChatCommandDefinition
{
    public class DisguiseChatCommand: IChatCommandListDefinition
    {
        public Dictionary<string, ChatCommandDetail> BuildChatCommands()
        {
            var builder = new ChatCommandBuilder();

            builder.Create("disguise")
                .Description("Sets a temporary disguise name. Example: /disguise Anakin")
                .Permissions(AuthorizationLevel.All)
                .Validate((user, args) =>
                {
                    if (!GetIsPC(user) || GetIsDM(user) || GetIsDMPossessed(user))
                    {
                        return "This command can only be used by player characters.";
                    }

                    var alias = string.Join(" ", args).Trim();
                    return Disguise.ValidateAlias(alias);
                })
                .Action((user, target, location, args) =>
                {
                    var alias = string.Join(" ", args).Trim();
                    if (!Disguise.SetAlias(user, alias))
                    {
                        SendMessageToPC(user, ColorToken.Red("Unable to set disguise at this time. Please relog and try again."));
                        return;
                    }

                    SendMessageToPC(user, $"You are now disguised as '{alias}'. Use /undisguise to remove it.");
                });

            builder.Create("undisguise")
                .Description("Removes your current disguise alias.")
                .Permissions(AuthorizationLevel.All)
                .Validate((user, args) =>
                {
                    if (!GetIsPC(user) || GetIsDM(user) || GetIsDMPossessed(user))
                    {
                        return "This command can only be used by player characters.";
                    }

                    if (args.Length > 0)
                    {
                        return "Usage: /undisguise";
                    }

                    if (!Disguise.IsDisguised(user))
                    {
                        return "You are not currently disguised.";
                    }

                    return string.Empty;
                })
                .Action((user, target, location, args) =>
                {
                    if (!Disguise.ClearAlias(user))
                    {
                        SendMessageToPC(user, ColorToken.Red("Unable to remove disguise at this time. Please relog and try again."));
                        return;
                    }

                    SendMessageToPC(user, "Your disguise has been removed.");
                });

            return builder.Build();
        }
    }
}
