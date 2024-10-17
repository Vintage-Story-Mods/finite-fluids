using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FiniteWater
{

	public class finiteBlockWaterflowing : BlockForFluidsLayer
	{

		public override void OnLoaded(ICoreAPI api)
		{
			base.OnLoaded(api);
			if (api.Side == EnumAppSide.Client)
			{
				(api as ICoreClientAPI).Settings.Int.AddWatcher("particleLevel", new OnSettingsChanged<int>(this.OnParticelLevelChanged));
				this.OnParticelLevelChanged(0);
			}
			this.ParticleProperties[0].SwimOnLiquid = true;
		}


		private void OnParticelLevelChanged(int newValue)
		{
			this.particleQuantity = 0.4f * (float)(this.api as ICoreClientAPI).Settings.Int["particleLevel"] / 100f;
		}


		public override float GetAmbientSoundStrength(IWorldAccessor world, BlockPos pos)
		{
			if (world.BlockAccessor.GetBlock(pos.X, pos.Y + 1, pos.Z).Replaceable >= 6000 && !world.BlockAccessor.GetBlock(pos.X, pos.Y + 1, pos.Z, 2).IsLiquid())
			{
				return 1f;
			}
			return 0f;
		}


		public override void OnAsyncClientParticleTick(IAsyncParticleManager manager, BlockPos pos, float windAffectednessAtPos, float secondsTicking)
		{
			// Check if BlockBehaviors is initialized
			BlockBehavior[] blockBehaviors = this.BlockBehaviors;
			if (blockBehaviors != null)
			{
				for (int i = 0; i < blockBehaviors.Length; i++)
				{
					blockBehaviors[i]?.OnAsyncClientParticleTick(manager, pos, windAffectednessAtPos, secondsTicking);
				}
			}

			// Ensure ParticleProperties is initialized and contains at least one element
			if (this.ParticleProperties == null || this.ParticleProperties.Length == 0)
			{
				return;
			}

			// Random chance for particles, early exit if not selected
			if (this.api.World.Rand.NextDouble() > (double)this.particleQuantity)
			{
				return;
			}

			AdvancedParticleProperties bps = this.ParticleProperties[0];
    
			// Check if base.PushVector is null
			if (base.PushVector == null)
			{
				return;
			}

			// Initialize particle properties
			bps.basePos.X = (double)pos.X;
			bps.basePos.Y = (double)pos.Y;
			bps.basePos.Z = (double)pos.Z;
			bps.Velocity[0].avg = (float)base.PushVector.X * 500f;
			bps.Velocity[1].avg = (float)base.PushVector.Y * 1000f;
			bps.Velocity[2].avg = (float)base.PushVector.Z * 500f;
			bps.GravityEffect.avg = 0.5f;
			bps.HsvaColor[3].avg = 180f * Math.Min(1f, secondsTicking / 7f);
			bps.Quantity.avg = 1f;
			bps.PosOffset[1].avg = 0.125f;
			bps.PosOffset[1].var = (float)this.LiquidLevel / 8f * 0.75f;
			bps.SwimOnLiquid = true;
			bps.Size.avg = 0.05f;
			bps.Size.var = 0f;
			bps.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 0.8f);

			// Spawn the particle
			manager.Spawn(bps);
		}


		private float particleQuantity = 0.2f;
	}
}
