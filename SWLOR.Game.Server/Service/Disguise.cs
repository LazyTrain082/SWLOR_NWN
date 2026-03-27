using System;
using System.Text.RegularExpressions;
using SWLOR.Game.Server.Core;
using SWLOR.Game.Server.Entity;
using SWLOR.NWN.API.NWNX;

namespace SWLOR.Game.Server.Service
{
    public static class Disguise
    {
        private const int MinAliasLength = 2;
        private const int MaxAliasLength = 32;
        private const int ExamineCooldownSeconds = 60 * 30;
        private const string PiercedKeyPrefix = "DISGUISE_PIERCED_";
        private const string ExamineAttemptKeyPrefix = "DISGUISE_EXAMINE_";
        private static readonly Regex _validAliasPattern = new(@"^[A-Za-z][A-Za-z' -]*[A-Za-z]$", RegexOptions.Compiled);

        public static string ValidateAlias(string alias)
        {
            var trimmed = alias?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return "Please enter a disguise name. Example: /disguise Anakin";
            }

            if (trimmed.Length < MinAliasLength || trimmed.Length > MaxAliasLength)
            {
                return $"Disguise names must be between {MinAliasLength} and {MaxAliasLength} characters.";
            }

            if (!_validAliasPattern.IsMatch(trimmed))
            {
                return "Disguise names may only contain letters, spaces, apostrophes, and hyphens.";
            }

            if (trimmed.Contains("  ") || trimmed.Contains("''") || trimmed.Contains("--"))
            {
                return "Disguise names cannot contain repeated punctuation or consecutive spaces.";
            }

            return string.Empty;
        }

        public static string GetAlias(uint player)
        {
            if (!GetIsPC(player) || GetIsDM(player))
            {
                return string.Empty;
            }

            var playerId = GetObjectUUID(player);
            var dbPlayer = DB.Get<Player>(playerId);
            return dbPlayer?.DisguiseAlias ?? string.Empty;
        }

        public static bool IsDisguised(uint player)
        {
            return !string.IsNullOrWhiteSpace(GetAlias(player));
        }

        public static bool SetAlias(uint player, string alias)
        {
            var playerId = GetObjectUUID(player);
            var dbPlayer = DB.Get<Player>(playerId);
            if (dbPlayer == null)
            {
                return false;
            }

            var trimmedAlias = alias.Trim();

            if (string.IsNullOrWhiteSpace(dbPlayer.DisguiseOriginalName))
            {
                dbPlayer.DisguiseOriginalName = GetName(player);
            }

            dbPlayer.DisguiseAlias = trimmedAlias;
            DB.Set(dbPlayer);
            ApplyAliasToAllViewers(player, trimmedAlias, resetPiercedState: true);
            return true;
        }

        public static bool ClearAlias(uint player)
        {
            var playerId = GetObjectUUID(player);
            var dbPlayer = DB.Get<Player>(playerId);
            if (dbPlayer == null)
            {
                return false;
            }

            dbPlayer.DisguiseAlias = string.Empty;
            DB.Set(dbPlayer);
            ClearAliasForAllViewers(player);
            return true;
        }

        [NWNEventHandler(ScriptName.OnExamineObjectAfter)]
        public static void OnExamineDisguisedPlayer()
        {
            var examiner = OBJECT_SELF;
            if (!GetIsPC(examiner) || GetIsDM(examiner) || GetIsDMPossessed(examiner))
            {
                return;
            }

            var target = StringToObject(EventsPlugin.GetEventData("EXAMINEE_OBJECT_ID"));
            if (!GetIsObjectValid(target) || !GetIsPC(target) || GetIsDM(target) || GetIsDMPossessed(target))
            {
                return;
            }

            if (!IsDisguised(target))
            {
                return;
            }

            if (HasPiercedDisguise(examiner, target))
            {
                PlayerPlugin.SetCreatureNameOverride(examiner, target, string.Empty);
                return;
            }

            if (IsOnExamineCooldown(examiner, target, out var remainingSeconds))
            {
                var remainingMinutes = (int)Math.Ceiling(remainingSeconds / 60.0);
                SendMessageToPC(examiner, ColorToken.Red($"You need to study this disguise longer before trying again ({remainingMinutes} minute(s) remaining)."));
                return;
            }

            SetLastExamineAttempt(examiner, target, GetCurrentUnixTime());

            var pierced = Random(100) < 50;
            if (!pierced)
            {
                return;
            }

            SetPiercedDisguise(examiner, target, true);
            PlayerPlugin.SetCreatureNameOverride(examiner, target, string.Empty);

            var trueName = GetName(target);
            SendMessageToPC(examiner, ColorToken.Green($"You pierce through the disguise. Their true identity is '{trueName}'."));
        }

        [NWNEventHandler(ScriptName.OnModuleEnter)]
        public static void OnPlayerEnter()
        {
            var enteringPlayer = GetEnteringObject();
            if (!GetIsPC(enteringPlayer))
            {
                return;
            }

            var enteringAlias = GetAlias(enteringPlayer);

            for (var viewer = GetFirstPC(); GetIsObjectValid(viewer); viewer = GetNextPC())
            {
                var viewerAlias = GetAlias(viewer);

                if (!string.IsNullOrWhiteSpace(viewerAlias))
                {
                    ApplyAliasForViewer(enteringPlayer, viewer, viewerAlias);
                }

                if (!string.IsNullOrWhiteSpace(enteringAlias))
                {
                    ApplyAliasForViewer(viewer, enteringPlayer, enteringAlias);
                }
            }
        }

        private static void ApplyAliasToAllViewers(uint disguisedPlayer, string alias, bool resetPiercedState)
        {
            for (var viewer = GetFirstPC(); GetIsObjectValid(viewer); viewer = GetNextPC())
            {
                if (resetPiercedState)
                {
                    SetPiercedDisguise(viewer, disguisedPlayer, false);
                    SetLastExamineAttempt(viewer, disguisedPlayer, 0);
                }

                ApplyAliasForViewer(viewer, disguisedPlayer, alias);
            }
        }

        private static void ClearAliasForAllViewers(uint disguisedPlayer)
        {
            for (var viewer = GetFirstPC(); GetIsObjectValid(viewer); viewer = GetNextPC())
            {
                SetPiercedDisguise(viewer, disguisedPlayer, false);
                SetLastExamineAttempt(viewer, disguisedPlayer, 0);
                PlayerPlugin.SetCreatureNameOverride(viewer, disguisedPlayer, string.Empty);
            }
        }

        private static void ApplyAliasForViewer(uint viewer, uint disguisedPlayer, string alias)
        {
            if (!GetIsObjectValid(viewer) || !GetIsObjectValid(disguisedPlayer) || !GetIsPC(viewer))
            {
                return;
            }

            if (HasPiercedDisguise(viewer, disguisedPlayer))
            {
                PlayerPlugin.SetCreatureNameOverride(viewer, disguisedPlayer, string.Empty);
                return;
            }

            PlayerPlugin.SetCreatureNameOverride(viewer, disguisedPlayer, alias);
        }

        private static string BuildPiercedKey(uint disguisedPlayer)
        {
            var targetId = GetObjectUUID(disguisedPlayer);
            return PiercedKeyPrefix + targetId;
        }

        private static bool HasPiercedDisguise(uint viewer, uint disguisedPlayer)
        {
            return GetLocalInt(viewer, BuildPiercedKey(disguisedPlayer)) == 1;
        }

        private static void SetPiercedDisguise(uint viewer, uint disguisedPlayer, bool isPierced)
        {
            SetLocalInt(viewer, BuildPiercedKey(disguisedPlayer), isPierced ? 1 : 0);
        }

        private static string BuildExamineAttemptKey(uint disguisedPlayer)
        {
            var targetId = GetObjectUUID(disguisedPlayer);
            return ExamineAttemptKeyPrefix + targetId;
        }

        private static bool IsOnExamineCooldown(uint viewer, uint disguisedPlayer, out int remainingSeconds)
        {
            remainingSeconds = 0;
            var lastAttempt = GetLocalInt(viewer, BuildExamineAttemptKey(disguisedPlayer));
            if (lastAttempt <= 0)
            {
                return false;
            }

            var now = GetCurrentUnixTime();
            var elapsed = now - lastAttempt;
            if (elapsed >= ExamineCooldownSeconds)
            {
                return false;
            }

            remainingSeconds = ExamineCooldownSeconds - elapsed;
            return true;
        }

        private static void SetLastExamineAttempt(uint viewer, uint disguisedPlayer, int unixTimestamp)
        {
            SetLocalInt(viewer, BuildExamineAttemptKey(disguisedPlayer), unixTimestamp);
        }

        private static int GetCurrentUnixTime()
        {
            return (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }
}
