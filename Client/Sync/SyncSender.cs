﻿using System;
using System.Threading;
using GTA;
using GTA.Math;
using GTA.Native;
using GTANetwork.Misc;
using GTANetwork.Util;
using GTANetwork.Sync;
using GTANetworkShared;
using Lidgren.Network;
using Vector3 = GTA.Math.Vector3;
using WeaponHash = GTA.WeaponHash;
using VehicleHash = GTA.VehicleHash;
using System.Collections.Generic;

namespace GTANetwork.Streamer
{
    internal static class SyncSender
    {
        private const int LIGHT_SYNC_RATE = 1500;
        private const int PURE_SYNC_RATE = 100;



        internal static void MainLoop()
        {
            bool lastPedData = false;
            int lastLightSyncSent = 0;

            while (true)
            {
                if (!Main.IsOnServer())
                {
                    Thread.Sleep(100);
                    continue;
                }

                object lastPacket;
                lock (SyncCollector.Lock)
                {
                    lastPacket = SyncCollector.LastSyncPacket;
                    SyncCollector.LastSyncPacket = null;
                }

                if (lastPacket == null) continue;
                try
                {
                    var data = lastPacket as PedData;
                    if (data != null)
                    {
                        var bin = PacketOptimization.WritePureSync(data);

                        var msg = Main.Client.CreateMessage();
                        msg.Write((byte) PacketType.PedPureSync);
                        msg.Write(bin.Length);
                        msg.Write(bin);

                        try
                        {
                            Main.Client.SendMessage(msg, NetDeliveryMethod.UnreliableSequenced,
                                (int) ConnectionChannel.PureSync);
                        }
                        catch (Exception ex)
                        {
                            Util.Util.SafeNotify("FAILED TO SEND DATA: " + ex.Message);
                            LogManager.LogException(ex, "SENDPLAYERDATA");
                        }

                        if (!lastPedData || Environment.TickCount - lastLightSyncSent > LIGHT_SYNC_RATE)
                        {
                            lastLightSyncSent = Environment.TickCount;

                            LogManager.DebugLog("SENDING LIGHT VEHICLE SYNC");

                            var lightBin = PacketOptimization.WriteLightSync(data);

                            var lightMsg = Main.Client.CreateMessage();
                            lightMsg.Write((byte) PacketType.PedLightSync);
                            lightMsg.Write(lightBin.Length);
                            lightMsg.Write(lightBin);
                            try
                            {
                                Main.Client.SendMessage(lightMsg, NetDeliveryMethod.ReliableSequenced,
                                    (int) ConnectionChannel.LightSync);
                            }
                            catch (Exception ex)
                            {
                                Util.Util.SafeNotify("FAILED TO SEND LIGHT DATA: " + ex.Message);
                                LogManager.LogException(ex, "SENDPLAYERDATA");
                            }

                            Main.BytesSent += lightBin.Length;
                            Main.MessagesSent++;
                        }

                        lastPedData = true;

                        lock (Main.AveragePacketSize)
                        {
                            Main.AveragePacketSize.Add(bin.Length);
                            if (Main.AveragePacketSize.Count > 10)
                                Main.AveragePacketSize.RemoveAt(0);
                        }

                        Main.BytesSent += bin.Length;
                        Main.MessagesSent++;
                    }
                    else
                    {
                        var bin = PacketOptimization.WritePureSync((VehicleData) lastPacket);

                        var msg = Main.Client.CreateMessage();
                        msg.Write((byte) PacketType.VehiclePureSync);
                        msg.Write(bin.Length);
                        msg.Write(bin);
                        try
                        {
                            Main.Client.SendMessage(msg, NetDeliveryMethod.UnreliableSequenced, (int) ConnectionChannel.PureSync);
                        }
                        catch (Exception ex)
                        {
                            Util.Util.SafeNotify("FAILED TO SEND DATA: " + ex.Message);
                            LogManager.LogException(ex, "SENDPLAYERDATA");
                        }

                        if (lastPedData || Environment.TickCount - lastLightSyncSent > LIGHT_SYNC_RATE)
                        {
                            lastLightSyncSent = Environment.TickCount;

                            LogManager.DebugLog("SENDING LIGHT VEHICLE SYNC");

                            var lightBin = PacketOptimization.WriteLightSync((VehicleData)lastPacket);

                            var lightMsg = Main.Client.CreateMessage();
                            lightMsg.Write((byte) PacketType.VehicleLightSync);
                            lightMsg.Write(lightBin.Length);
                            lightMsg.Write(lightBin);
                            try
                            {
                                Main.Client.SendMessage(lightMsg, NetDeliveryMethod.ReliableSequenced, (int) ConnectionChannel.LightSync);
                            }
                            catch (Exception ex)
                            {
                                Util.Util.SafeNotify("FAILED TO SEND LIGHT DATA: " + ex.Message);
                                LogManager.LogException(ex, "SENDPLAYERDATA");
                            }

                            Main.BytesSent += lightBin.Length;
                            Main.MessagesSent++;
                        }

                        lastPedData = false;

                        lock (Main.AveragePacketSize)
                        {
                            Main.AveragePacketSize.Add(bin.Length);
                            if (Main.AveragePacketSize.Count > 10)
                                Main.AveragePacketSize.RemoveAt(0);
                        }

                        Main.BytesSent += bin.Length;
                        Main.MessagesSent++;
                    }
                }
                catch (Exception ex)
                {
                    LogManager.LogException(ex, "SYNCSENDER");
                }

                LogManager.DebugLog("END SYNC SEND");

                Thread.Sleep(PURE_SYNC_RATE);
            }
        }
    }

    public class SyncCollector : Script
    {
        internal static bool ForceAimData;
        internal static object LastSyncPacket;
        internal static object Lock = new object();

        private static bool _lastShooting;
        private static bool _lastBullet;
        private static DateTime _lastShot;
        private static bool sent = true;


        public SyncCollector()
        {
            var t = new Thread(SyncSender.MainLoop);
            t.IsBackground = true;
            t.Start();

            Tick += OnTick;
        }

        internal void OnTick(object sender, EventArgs e)
        {
            if (!Main.IsOnServer()) return;
            var player = Game.Player.Character;

            if (player.IsInVehicle())
            {
                var veh = player.CurrentVehicle;

                var horn = Game.Player.IsPressingHorn;
                var siren = veh.SirenActive;
                var vehdead = veh.IsDead;

                var obj = new VehicleData();
                obj.Position = veh.Position.ToLVector();
                obj.VehicleHandle = Main.NetEntityHandler.EntityToNet(player.CurrentVehicle.Handle);
                obj.Quaternion = veh.Rotation.ToLVector();
                obj.PedModelHash = player.Model.Hash;
                obj.PlayerHealth = (byte) Util.Util.Clamp(0, player.Health, 255);
                obj.VehicleHealth = veh.EngineHealth;
                obj.Velocity = veh.Velocity.ToLVector();
                obj.PedArmor = (byte)player.Armor;
                obj.RPM = veh.CurrentRPM;
                obj.VehicleSeat = (short)Util.Util.GetPedSeat(player);
                obj.Flag = 0;
                obj.Steering = veh.SteeringAngle;

                if (horn)
                    obj.Flag |= (byte)VehicleDataFlags.PressingHorn;
                if (siren)
                    obj.Flag |= (byte)VehicleDataFlags.SirenActive;
                if (vehdead)
                    obj.Flag |= (byte)VehicleDataFlags.VehicleDead;
                if (player.IsDead)
                    obj.Flag |= (short) VehicleDataFlags.PlayerDead;

                if (Util.Util.GetResponsiblePed(veh).Handle == player.Handle)
                    obj.Flag |= (byte)VehicleDataFlags.Driver;

                if (veh.IsInBurnout)
                    obj.Flag |= (byte)VehicleDataFlags.BurnOut;

                if (ForceAimData)
                    obj.Flag |= (byte) VehicleDataFlags.HasAimData;

                if (player.IsSubtaskActive(167) || player.IsSubtaskActive(168))
                {
                    obj.Flag |= (short) VehicleDataFlags.ExitingVehicle;
                }

                if (!WeaponDataProvider.DoesVehicleSeatHaveGunPosition((VehicleHash)veh.Model.Hash, Util.Util.GetPedSeat(player)) &&
                    WeaponDataProvider.DoesVehicleSeatHaveMountedGuns((VehicleHash)veh.Model.Hash) &&
                    Util.Util.GetPedSeat(player) == -1)
                {
                    obj.Flag |= (byte)VehicleDataFlags.HasAimData;
                    obj.AimCoords = new GTANetworkShared.Vector3(0, 0, 0);
                    obj.WeaponHash = Main.GetCurrentVehicleWeaponHash(player);
                    if (Game.IsEnabledControlPressed(0, Control.VehicleFlyAttack))
                        obj.Flag |= (byte)VehicleDataFlags.Shooting;
                }
                else if (WeaponDataProvider.DoesVehicleSeatHaveGunPosition((VehicleHash)veh.Model.Hash, Util.Util.GetPedSeat(player)))
                {
                    obj.Flag |= (byte)VehicleDataFlags.HasAimData;
                    obj.WeaponHash = 0;
                    obj.AimCoords = Main.RaycastEverything(new Vector2(0, 0)).ToLVector();
                    if (Game.IsEnabledControlPressed(0, Control.VehicleAttack))
                        obj.Flag |= (byte)VehicleDataFlags.Shooting;
                }
                else
                {
                    if ((player.IsSubtaskActive(200) || player.IsSubtaskActive(190)) &&
                        Game.IsEnabledControlPressed(0, Control.Attack) &&
                        player.Weapons.Current?.AmmoInClip != 0)
                    {
                        obj.Flag |= (byte)VehicleDataFlags.Shooting;
                        obj.Flag |= (byte)VehicleDataFlags.HasAimData;
                    }

                    if (((player.IsSubtaskActive(200) || player.IsSubtaskActive(190)) &&
                         player.Weapons.Current?.AmmoInClip != 0) ||
                        (player.Weapons.Current?.Hash == WeaponHash.Unarmed &&
                         player.IsSubtaskActive(200)))
                    {
                        obj.Flag |= (byte)VehicleDataFlags.Aiming;
                        obj.Flag |= (byte)VehicleDataFlags.HasAimData;
                    }

                    var outputArg = new OutputArgument();
                    Function.Call(Hash.GET_CURRENT_PED_WEAPON, player, outputArg, true);
                    obj.WeaponHash = outputArg.GetResult<int>();

                    lock (Lock)
                    {
                        if (LastSyncPacket != null && LastSyncPacket is VehicleData &&
                            WeaponDataProvider.NeedsFakeBullets(obj.WeaponHash.Value) &&
                            (((VehicleData) LastSyncPacket).Flag & (byte) VehicleDataFlags.Shooting) != 0)
                        {
                            obj.Flag |= (byte) VehicleDataFlags.Shooting;
                            obj.Flag |= (byte)VehicleDataFlags.HasAimData;
                        }
                    }

                    obj.AimCoords = Main.RaycastEverything(new Vector2(0, 0)).ToLVector();
                }

                Vehicle trailer;

                if ((VehicleHash)veh.Model.Hash == VehicleHash.TowTruck ||
                    (VehicleHash)veh.Model.Hash == VehicleHash.TowTruck2)
                    trailer = veh.TowedVehicle;
                else if ((VehicleHash)veh.Model.Hash == VehicleHash.Cargobob ||
                         (VehicleHash)veh.Model.Hash == VehicleHash.Cargobob2 ||
                         (VehicleHash)veh.Model.Hash == VehicleHash.Cargobob3 ||
                         (VehicleHash)veh.Model.Hash == VehicleHash.Cargobob4)
                    trailer = SyncEventWatcher.GetVehicleCargobobVehicle(veh);
                else trailer = SyncEventWatcher.GetVehicleTrailerVehicle(veh);

                if (trailer != null && trailer.Exists())
                {
                    obj.Trailer = trailer.Position.ToLVector();
                }

                if (Util.Util.GetResponsiblePed(veh) == player)
                    obj.DamageModel = veh.GetVehicleDamageModel();

                lock (Lock)
                {
                    LastSyncPacket = obj;
                }
            }
            else
            {
                bool aiming = player.IsSubtaskActive(ESubtask.AIMED_SHOOTING_ON_FOOT) || player.IsSubtaskActive(ESubtask.AIMING_THROWABLE); // Game.IsControlPressed(0, GTA.Control.Aim);
                bool shooting = Function.Call<bool>(Hash.IS_PED_SHOOTING, player.Handle);

                GTA.Math.Vector3 aimCoord = new Vector3();
                if (aiming || shooting)
                {
                    aimCoord = Main.RaycastEverything(new Vector2(0, 0));
                }

                var obj = new PedData();
                obj.AimCoords = aimCoord.ToLVector();
                obj.Position = player.Position.ToLVector();
                obj.Quaternion = player.Rotation.ToLVector();
                obj.PedArmor = (byte)player.Armor;
                obj.PedModelHash = player.Model.Hash;
                obj.WeaponHash = (int)player.Weapons.Current.Hash;
                obj.WeaponAmmo = player.Weapons.Current.Ammo;
                obj.PlayerHealth = (byte)Util.Util.Clamp(0, player.Health, 255);
                obj.Velocity = player.Velocity.ToLVector();

                obj.Flag = 0;

                if (player.IsRagdoll)
                    obj.Flag |= (int)PedDataFlags.Ragdoll;
                if (Function.Call<int>(Hash.GET_PED_PARACHUTE_STATE, player.Handle) == 0 &&
                    player.IsInAir)
                    obj.Flag |= (int)PedDataFlags.InFreefall;
                if (player.IsInMeleeCombat)
                    obj.Flag |= (int)PedDataFlags.InMeleeCombat;
                if (aiming || shooting)
                    obj.Flag |= (int)PedDataFlags.Aiming;
                if ((player.IsInMeleeCombat && Game.IsControlJustPressed(0, Control.Attack)))
                    obj.Flag |= (int) PedDataFlags.Shooting;
                if (Function.Call<bool>(Hash.IS_PED_JUMPING, player.Handle))
                    obj.Flag |= (int)PedDataFlags.Jumping;
                if (Function.Call<int>(Hash.GET_PED_PARACHUTE_STATE, player.Handle) == 2)
                    obj.Flag |= (int)PedDataFlags.ParachuteOpen;
                if (player.IsInCover())
                    obj.Flag |= (int)PedDataFlags.IsInCover;
                if (!Function.Call<bool>((Hash)0x6A03BF943D767C93, player))
                    obj.Flag |= (int)PedDataFlags.IsInLowerCover;
                if (player.IsInCoverFacingLeft)
                    obj.Flag |= (int)PedDataFlags.IsInCoverFacingLeft;
                if (player.IsReloading)
                    obj.Flag |= (int)PedDataFlags.IsReloading;
                if (ForceAimData)
                    obj.Flag |= (int)PedDataFlags.HasAimData;
                if (player.IsSubtaskActive(ESubtask.USING_LADDER))
                    obj.Flag |= (int) PedDataFlags.IsOnLadder;
                if (Function.Call<bool>(Hash.IS_PED_CLIMBING, player) && !player.IsSubtaskActive(ESubtask.USING_LADDER))
                    obj.Flag |= (int) PedDataFlags.IsVaulting;
                if (Function.Call<bool>(Hash.IS_ENTITY_ON_FIRE, player))
                    obj.Flag |= (int) PedDataFlags.OnFire;
                if (player.IsDead)
                    obj.Flag |= (int)PedDataFlags.PlayerDead;

                if (player.IsSubtaskActive(168))
                {
                    obj.Flag |= (int) PedDataFlags.ClosingVehicleDoor;
                }

                if (player.IsSubtaskActive(161) || player.IsSubtaskActive(162) || player.IsSubtaskActive(163) ||
                    player.IsSubtaskActive(164))
                {
                    obj.Flag |= (int)PedDataFlags.EnteringVehicle;

                    obj.VehicleTryingToEnter =
                        Main.NetEntityHandler.EntityToNet(Function.Call<int>(Hash.GET_VEHICLE_PED_IS_TRYING_TO_ENTER,
                            player));
                    
                    obj.SeatTryingToEnter = (sbyte)
                        Function.Call<int>(Hash.GET_SEAT_PED_IS_TRYING_TO_ENTER,
                            player);
                }

                obj.Speed = Main.GetPedWalkingSpeed(player);

                lock (Lock)
                {
                    LastSyncPacket = obj;
                }

                bool sendShootingPacket;

                if (!WeaponDataProvider.IsWeaponAutomatic(unchecked ((GTANetworkShared.WeaponHash) obj.WeaponHash.Value)))
                {
                    sendShootingPacket = (shooting && !player.IsSubtaskActive(ESubtask.AIMING_PREVENTED_BY_OBSTACLE) && !player.IsSubtaskActive(ESubtask.MELEE_COMBAT));
                }
                else
                {
                    if (!_lastShooting && !player.IsSubtaskActive(ESubtask.MELEE_COMBAT))
                    {
                        sendShootingPacket = (shooting && !player.IsSubtaskActive(ESubtask.AIMING_PREVENTED_BY_OBSTACLE) &&
                                              !player.IsSubtaskActive(ESubtask.MELEE_COMBAT)) ||
                                              ((player.IsInMeleeCombat || player.IsSubtaskActive(ESubtask.MELEE_COMBAT)) &&
                                              Game.IsEnabledControlPressed(0, Control.Attack));
                    }
                    else
                    {
                        sendShootingPacket = (!player.IsSubtaskActive(ESubtask.AIMING_PREVENTED_BY_OBSTACLE) &&
                                              !player.IsSubtaskActive(ESubtask.MELEE_COMBAT) &&
                                              !player.IsReloading &&
                                              player.Weapons.Current.AmmoInClip > 0 &&
                                              Game.IsEnabledControlPressed(0, Control.Attack)) ||
                                             ((player.IsInMeleeCombat || player.IsSubtaskActive(ESubtask.MELEE_COMBAT)) &&
                                              Game.IsEnabledControlPressed(0, Control.Attack));
                    }

                    if (!sendShootingPacket && _lastShooting && !_lastBullet)
                    {
                        _lastBullet = true;
                        _lastShooting = false;
                        return;
                    }
                }

                _lastBullet = false;

                if (player.IsRagdoll) sendShootingPacket = false;

                if (!player.IsSubtaskActive(ESubtask.MELEE_COMBAT) && player.Weapons.Current.Ammo == 0) sendShootingPacket = false;

                if (sendShootingPacket && !_lastShooting && DateTime.Now.Subtract(_lastShot).TotalMilliseconds > 50)
                {
                    Util.Util.SafeNotify("Sending BPacket " + DateTime.Now.Millisecond);
                    sent = false;
                    _lastShooting = true;
                    _lastShot = DateTime.Now;

                    var msg = Main.Client.CreateMessage();
                    byte[] bin;

                    var syncPlayer = Main.GetPedWeHaveDamaged();

                    if (syncPlayer != null)
                    {
                        bin = PacketOptimization.WriteBulletSync(0, true, syncPlayer.RemoteHandle);
                        msg.Write((byte)PacketType.BulletPlayerSync);
                    }
                    else
                    {
                        bin = PacketOptimization.WriteBulletSync(0, true, aimCoord.ToLVector());
                        msg.Write((byte)PacketType.BulletSync);
                    }

                    msg.Write(bin.Length);
                    msg.Write(bin);
                    Main.Client.SendMessage(msg, NetDeliveryMethod.ReliableOrdered, (int)ConnectionChannel.BulletSync);
                    Main.BytesSent += bin.Length;
                    Main.MessagesSent++;
                }
                else if (!sendShootingPacket && !sent && DateTime.Now.Subtract(_lastShot).TotalMilliseconds > 50)
                {
                    Util.Util.SafeNotify("Sending NPacket " + DateTime.Now.Millisecond);
                    sent = true;
                    _lastShooting = false;
                    _lastShot = DateTime.Now;

                    var msg = Main.Client.CreateMessage();

                    byte[] bin = PacketOptimization.WriteBulletSync(0, false, aimCoord.ToLVector());
                    msg.Write((byte)PacketType.BulletSync);

                    msg.Write(bin.Length);
                    msg.Write(bin);
                    Main.Client.SendMessage(msg, NetDeliveryMethod.ReliableOrdered, (int)ConnectionChannel.BulletSync);
                    Main.BytesSent += bin.Length;
                    Main.MessagesSent++;
                }

            }
        }


    }
}