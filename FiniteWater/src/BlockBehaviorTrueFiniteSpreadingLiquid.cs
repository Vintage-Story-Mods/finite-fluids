using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace FiniteWater
{

	public class BlockBehaviorTrueFiniteSpreadingLiquid : BlockBehavior
	{
		public BlockBehaviorTrueFiniteSpreadingLiquid(Block block)
			: base(block)
		{
		}

		public override void Initialize(JsonObject properties)
		{
			base.Initialize(properties);
			this.spreadDelay = properties["spreadDelay"].AsInt(0);
			this.collisionReplaceSound = BlockBehaviorTrueFiniteSpreadingLiquid.CreateAssetLocation(properties, "sounds/", "liquidCollisionSound");
			this.liquidSourceCollisionReplacement = BlockBehaviorTrueFiniteSpreadingLiquid.CreateAssetLocation(properties, "sourceReplacementCode");
			this.liquidFlowingCollisionReplacement = BlockBehaviorTrueFiniteSpreadingLiquid.CreateAssetLocation(properties, "flowingReplacementCode");
			JsonObject jsonObject = properties["collidesWith"];
			this.collidesWith = ((jsonObject != null) ? jsonObject.AsString(null) : null);
		}

		public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack, ref EnumHandling handling)
		{
			if (world is IServerWorldAccessor)
			{
				world.RegisterCallbackUnique(new Action<IWorldAccessor, BlockPos, float>(this.OnDelayedWaterUpdateCheck), blockSel.Position, this.spreadDelay);
			}
			return base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack, ref handling);
		}

		public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos, ref EnumHandling handled)
		{
			handled = EnumHandling.PreventDefault;
			if (world is IServerWorldAccessor)
			{
				world.RegisterCallbackUnique(new Action<IWorldAccessor, BlockPos, float>(this.OnDelayedWaterUpdateCheck), pos, this.spreadDelay);
			}
		}

		private void OnDelayedWaterUpdateCheck(IWorldAccessor world, BlockPos pos, float dt)
		{
			this.SpreadAndUpdateLiquidLevels(world, pos);
			world.BulkBlockAccessor.Commit();
			Block block = world.BlockAccessor.GetBlock(pos, 2);
			if (block.HasBehavior<BlockBehaviorTrueFiniteSpreadingLiquid>(false))
			{
				this.updateOwnFlowDir(block, world, pos);
			}
			BlockPos npos = pos.Copy();
			foreach (Cardinal val in Cardinal.ALL)
			{
				npos.Set(pos.X + val.Normali.X, pos.Y, pos.Z + val.Normali.Z);
				Block neib = world.BlockAccessor.GetBlock(npos, 2);
				if (neib.HasBehavior<BlockBehaviorTrueFiniteSpreadingLiquid>(false))
				{
					this.updateOwnFlowDir(neib, world, npos);
				}
			}
		}

		private void SpreadAndUpdateLiquidLevels(IWorldAccessor world, BlockPos pos)
		{
			Block ourBlock = world.BlockAccessor.GetBlock(pos, 2);
			int liquidLevel = ourBlock.LiquidLevel;
			int totalVolume = liquidLevel; // Total volume of water we have to spread

			List<BlockPos> spreadPositions = new List<BlockPos>();

			// First, attempt to spread downward with a strong bias
			BlockPos downPos = pos.DownCopy();
			Block downBlock = world.BlockAccessor.GetBlock(downPos, 2);
			Block downSolidBlock = world.BlockAccessor.GetBlock(downPos, 1);

			// Prefer flowing downward
			bool spreadOccurred = false;
			if (CanSpreadIntoBlock(ourBlock, downSolidBlock, pos, downPos, BlockFacing.DOWN, world) &&
			    (downBlock.IsLiquid() ? downBlock.LiquidLevel : 0) < 7)
			{
				// Calculate how much water can be transferred downward
				int fillAmount = Math.Min(7 - downBlock.LiquidLevel, totalVolume);

				// Update the downward block
				int newDownLevel = downBlock.LiquidLevel + fillAmount;
				world.BulkBlockAccessor.SetBlock(GetLiquidBlockId(world, downPos, ourBlock, newDownLevel), downPos, 2);

				// Reduce the total volume of the current block
				totalVolume -= fillAmount;
				spreadOccurred = true;

				if (totalVolume <= 0)
				{
					world.BulkBlockAccessor.SetBlock(0, pos, 2); // Remove the water source block
					world.BulkBlockAccessor.Commit();
					return;
				}
			}

			// If water cannot flow downwards fully, attempt to spread horizontally with bias
			if (totalVolume > 1)
			{
				Dictionary<BlockPos, int> flowBias = new Dictionary<BlockPos, int>();

				// Assign a high bias to the current flow direction, low bias to other directions
				foreach (BlockFacing facing in BlockFacing.HORIZONTALS)
				{
					BlockPos adjPos = pos.AddCopy(facing);
					Block adjBlock = world.BlockAccessor.GetBlock(adjPos, 2);
					Block solidBlock = world.BlockAccessor.GetBlock(adjPos, 1);

					if (CanSpreadIntoBlock(ourBlock, solidBlock, pos, adjPos, facing, world))
					{
						int existingLevel = adjBlock.IsLiquid() ? adjBlock.LiquidLevel : 0;
						if (existingLevel < 7)
						{
							spreadPositions.Add(adjPos);

							// Bias towards the direction of flow
							int bias = CalculateFlowBias(facing, ourBlock, world, pos);
							flowBias[adjPos] = bias;
						}
					}
				}

				if (spreadPositions.Count > 0)
				{
					// Sort spread positions based on their flow bias, preferring higher bias positions
					spreadPositions.Sort((a, b) => flowBias[b].CompareTo(flowBias[a]));

					int volumeToSpread = totalVolume - 1; // Keep at least 1 in the source block

					int totalCapacity = 0;
					foreach (BlockPos spreadPos in spreadPositions)
					{
						Block spreadBlock = world.BlockAccessor.GetBlock(spreadPos, 2);
						totalCapacity += (7 - spreadBlock.LiquidLevel);
					}

					volumeToSpread = Math.Min(volumeToSpread, totalCapacity);

					if (volumeToSpread > 0)
					{
						int spreadCount = spreadPositions.Count;
						int volumePerBlock = volumeToSpread / spreadCount;
						int remainder = volumeToSpread % spreadCount;

						spreadPositions.Shuffle(world.Rand);

						foreach (BlockPos spreadPos in spreadPositions)
						{
							Block spreadBlock = world.BlockAccessor.GetBlock(spreadPos, 2);
							int fillAmount = Math.Min(volumePerBlock + (remainder > 0 ? 1 : 0),
								7 - spreadBlock.LiquidLevel);

							if (remainder > 0)
							{
								remainder--;
							}

							int newLevel = spreadBlock.LiquidLevel + fillAmount;
							world.BulkBlockAccessor.SetBlock(GetLiquidBlockId(world, spreadPos, ourBlock, newLevel),
								spreadPos, 2);

							totalVolume -= fillAmount;

							if (totalVolume <= 0)
							{
								break;
							}
						}
					}
				}
			}

			// Update the source block if any volume is left
			if (totalVolume > 0)
			{
				world.BulkBlockAccessor.SetBlock(GetLiquidBlockId(world, pos, ourBlock, totalVolume), pos, 2);
			}
			else
			{
				world.BulkBlockAccessor.SetBlock(0, pos, 2); // Remove the source block
			}

			world.BulkBlockAccessor.Commit();
		}

		private int CalculateFlowBias(BlockFacing direction, Block ourBlock, IWorldAccessor world, BlockPos pos)
		{
			// Strongly prefer downward flow
			if (direction == BlockFacing.DOWN)
			{
				return 100;  // Maximum bias for downward flow
			}

			// Check if the current block has a flow direction (e.g., based on previous spread or terrain slope)
			// Assign higher bias to directions that align with the flow
			BlockPos adjacentPos = pos.AddCopy(direction);
			Block adjacentBlock = world.BlockAccessor.GetBlock(adjacentPos, 2);
    
			// If there's a downward block below this neighbor, prefer flow in this direction
			BlockPos belowAdjacentPos = adjacentPos.DownCopy();
			Block belowAdjacentBlock = world.BlockAccessor.GetBlock(belowAdjacentPos, 2);

			if (belowAdjacentBlock.LiquidLevel < 7)
			{
				return 80;  // Strong bias if the adjacent block can flow downward
			}

			// Medium bias for horizontal flow in case it aligns with an existing slope or path
			return 30;  // Increased from 5 to create stronger flow preference for horizontal paths
		}

		public bool CanSpreadIntoBlock(Block ourBlock, Block solidBlock, BlockPos pos, BlockPos npos, BlockFacing facing, IWorldAccessor world)
		{
			// Check for liquid barriers
			if (solidBlock.GetLiquidBarrierHeightOnSide(facing, pos) >= (float)ourBlock.LiquidLevel / 7f)
			{
				return false;
			}

			Block neighborSolidBlock = world.BlockAccessor.GetBlock(npos, 1);
			if (neighborSolidBlock.GetLiquidBarrierHeightOnSide(facing.Opposite, npos) >= (float)ourBlock.LiquidLevel / 7f)
			{
				return false;
			}

			// Get the liquid block at the neighboring position
			Block neighborLiquid = world.BlockAccessor.GetBlock(npos, 2);

			// If the neighboring block is the same liquid type and not full, we can spread, but we avoid blocks with very high levels (like 6/7)
			if (neighborLiquid.LiquidCode == ourBlock.LiquidCode && neighborLiquid.LiquidLevel < 6)
			{
				return true;
			}

			// If the neighboring block is empty or replaceable
			if (!neighborLiquid.IsLiquid() && neighborLiquid.Replaceable >= ourBlock.Replaceable)
			{
				return true;
			}

			return false;
		}

		private int CountNearbySourceBlocks(IBlockAccessor blockAccessor, BlockPos pos, Block ourBlock)
		{
			BlockPos qpos = pos.Copy();
			int nearbySourceBlockCount = 0;
			for (int i = 0; i < BlockFacing.HORIZONTALS.Length; i++)
			{
				BlockFacing.HORIZONTALS[i].IterateThruFacingOffsets(qpos);
				Block nblock = blockAccessor.GetBlock(qpos, 2);
				if (this.IsSameLiquid(ourBlock, nblock) && this.IsLiquidSourceBlock(nblock))
				{
					nearbySourceBlockCount++;
				}
			}
			return nearbySourceBlockCount;
		}

		private int CountNearbyDiagonalSources(IBlockAccessor blockAccessor, BlockPos pos, Block ourBlock)
		{
			BlockPos npos = pos.Copy();
			int nearbySourceBlockCount = 0;
			foreach (Cardinal val in Cardinal.ALL)
			{
				if (val.IsDiagnoal)
				{
					npos.Set(pos.X + val.Normali.X, pos.Y, pos.Z + val.Normali.Z);
					Block nblock = blockAccessor.GetBlock(npos, 2);
					if (this.IsSameLiquid(ourBlock, nblock) && this.IsLiquidSourceBlock(nblock))
					{
						nearbySourceBlockCount++;
					}
				}
			}
			return nearbySourceBlockCount;
		}

		private void FlowTowardDownwardPaths(List<PosAndDist> downwardPaths, Block liquidBlock, Block solidBlock, BlockPos pos, IWorldAccessor world)
		{
			foreach (PosAndDist pod in downwardPaths)
			{
				if (this.CanSpreadIntoBlock(liquidBlock, solidBlock, pos, pod.pos, pod.pos.FacingFrom(pos), world))
				{
					Block neighborLiquid = world.BlockAccessor.GetBlock(pod.pos, 2);
					if (this.IsDifferentCollidableLiquid(liquidBlock, neighborLiquid))
					{
						this.ReplaceLiquidBlock(neighborLiquid, pod.pos, world);
					}
					else
					{
						this.SpreadLiquid(this.GetLessLiquidBlockId(world, pod.pos, liquidBlock), pod.pos, world);
					}
				}
			}
		}

		private bool TrySpreadDownwards(IWorldAccessor world, Block ourSolid, Block ourBlock, BlockPos pos)
		{
			BlockPos npos = pos.DownCopy(1);
			Block belowLiquid = world.BlockAccessor.GetBlock(npos, 2);
			if (this.CanSpreadIntoBlock(ourBlock, ourSolid, pos, npos, BlockFacing.DOWN, world))
			{
				if (this.IsDifferentCollidableLiquid(ourBlock, belowLiquid))
				{
					this.ReplaceLiquidBlock(belowLiquid, npos, world);
					this.TryFindSourceAndSpread(npos, world);
				}
				else
				{
					bool fillWithSource = false;
					if (this.IsLiquidSourceBlock(ourBlock))
					{
						if (this.CountNearbySourceBlocks(world.BlockAccessor, npos, ourBlock) > 1)
						{
							fillWithSource = true;
						}
						else
						{
							npos.Y--;
							if (world.BlockAccessor.GetBlock(npos, 4).CanAttachBlockAt(world.BlockAccessor, ourBlock, npos, BlockFacing.UP, null) || this.IsLiquidSourceBlock(world.BlockAccessor.GetBlock(npos, 2)))
							{
								fillWithSource = this.CountNearbySourceBlocks(world.BlockAccessor, pos, ourBlock) >= 2;
							}
							npos.Y++;
						}
					}
					this.SpreadLiquid(fillWithSource ? ourBlock.BlockId : this.GetFallingLiquidBlockId(ourBlock, world), npos, world);
				}
				return true;
			}
			return !this.IsLiquidSourceBlock(ourBlock) || !this.IsLiquidSourceBlock(belowLiquid);
		}

		private void TrySpreadHorizontal(Block ourblock, Block ourSolid, IWorldAccessor world, BlockPos pos)
		{
			foreach (BlockFacing facing in BlockFacing.HORIZONTALS)
			{
				this.TrySpreadIntoBlock(ourblock, ourSolid, pos, pos.AddCopy(facing), facing, world);
			}
		}

		private void ReplaceLiquidBlock(Block liquidBlock, BlockPos pos, IWorldAccessor world)
		{
			Block replacementBlock = this.GetReplacementBlock(liquidBlock, world);
			if (replacementBlock != null)
			{
				world.BulkBlockAccessor.SetBlock(replacementBlock.BlockId, pos);
				BlockBehaviorBreakIfFloating bh = replacementBlock.GetBehavior<BlockBehaviorBreakIfFloating>();
				if (bh != null && bh.IsSurroundedByNonSolid(world, pos))
				{
					world.BulkBlockAccessor.SetBlock(replacementBlock.BlockId, pos.DownCopy(1));
				}
				this.UpdateNeighbouringLiquids(pos, world);
				this.GenerateSteamParticles(pos, world);
				world.PlaySoundAt(this.collisionReplaceSound, pos, 0.0, null, true, 16f, 1f);
			}
		}

		private void SpreadLiquid(int blockId, BlockPos pos, IWorldAccessor world)
		{
			world.BulkBlockAccessor.SetBlock(blockId, pos, 2);
			world.RegisterCallbackUnique(new Action<IWorldAccessor, BlockPos, float>(this.OnDelayedWaterUpdateCheck), pos, this.spreadDelay);
			Block ourBlock = world.GetBlock(blockId);
			this.TryReplaceNearbyLiquidBlocks(ourBlock, pos, world);
		}

		private void updateOwnFlowDir(Block block, IWorldAccessor world, BlockPos pos)
		{
			int blockId = this.GetLiquidBlockId(world, pos, block, block.LiquidLevel);
			if (block.BlockId != blockId)
			{
				world.BlockAccessor.SetBlock(blockId, pos, 2);
			}
		}

		private void TryReplaceNearbyLiquidBlocks(Block ourBlock, BlockPos pos, IWorldAccessor world)
		{
			BlockPos npos = pos.Copy();
			BlockFacing[] horizontals = BlockFacing.HORIZONTALS;
			for (int i = 0; i < horizontals.Length; i++)
			{
				horizontals[i].IterateThruFacingOffsets(npos);
				Block neib = world.BlockAccessor.GetBlock(npos, 2);
				if (this.IsDifferentCollidableLiquid(ourBlock, neib))
				{
					this.ReplaceLiquidBlock(ourBlock, npos, world);
				}
			}
		}

		private bool TryFindSourceAndSpread(BlockPos startingPos, IWorldAccessor world)
		{
			BlockPos sourceBlockPos = startingPos.UpCopy(1);
			Block sourceBlock = world.BlockAccessor.GetBlock(sourceBlockPos, 2);
			while (sourceBlock.IsLiquid())
			{
				if (this.IsLiquidSourceBlock(sourceBlock))
				{
					Block ourSolid = world.BlockAccessor.GetBlock(sourceBlockPos, 1);
					this.TrySpreadHorizontal(sourceBlock, ourSolid, world, sourceBlockPos);
					return true;
				}
				sourceBlockPos.Add(0, 1, 0);
				sourceBlock = world.BlockAccessor.GetBlock(sourceBlockPos, 2);
			}
			return false;
		}

		private void GenerateSteamParticles(BlockPos pos, IWorldAccessor world)
		{
			float num = 50f;
			float maxQuantity = 100f;
			int color = ColorUtil.ToRgba(100, 225, 225, 225);
			Vec3d minPos = new Vec3d();
			Vec3d addPos = new Vec3d();
			Vec3f minVelocity = new Vec3f(-0.25f, 0.1f, -0.25f);
			Vec3f maxVelocity = new Vec3f(0.25f, 0.1f, 0.25f);
			float lifeLength = 2f;
			float gravityEffect = -0.015f;
			float minSize = 0.1f;
			float maxSize = 0.1f;
			SimpleParticleProperties steamParticles = new SimpleParticleProperties(num, maxQuantity, color, minPos, addPos, minVelocity, maxVelocity, lifeLength, gravityEffect, minSize, maxSize, EnumParticleModel.Quad);
			steamParticles.Async = true;
			steamParticles.MinPos.Set(pos.ToVec3d().AddCopy(0.5, 1.1, 0.5));
			steamParticles.AddPos.Set(new Vec3d(0.5, 1.0, 0.5));
			steamParticles.SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEARINCREASE, 1f);
			world.SpawnParticles(steamParticles, null);
		}

		private void UpdateNeighbouringLiquids(BlockPos pos, IWorldAccessor world)
		{
			BlockPos npos = pos.DownCopy(1);
			if (world.BlockAccessor.GetBlock(npos, 2).HasBehavior<BlockBehavior>(false))
			{
				world.RegisterCallbackUnique(new Action<IWorldAccessor, BlockPos, float>(this.OnDelayedWaterUpdateCheck), npos.Copy(), this.spreadDelay);
			}
			npos.Up(2);
			if (world.BlockAccessor.GetBlock(npos, 2).HasBehavior<BlockBehaviorTrueFiniteSpreadingLiquid>(false))
			{
				world.RegisterCallbackUnique(new Action<IWorldAccessor, BlockPos, float>(this.OnDelayedWaterUpdateCheck), npos.Copy(), this.spreadDelay);
			}
			foreach (Cardinal val in Cardinal.ALL)
			{
				npos.Set(pos.X + val.Normali.X, pos.Y, pos.Z + val.Normali.Z);
				if (world.BlockAccessor.GetBlock(npos, 2).HasBehavior<BlockBehaviorTrueFiniteSpreadingLiquid>(false))
				{
					world.RegisterCallbackUnique(new Action<IWorldAccessor, BlockPos, float>(this.OnDelayedWaterUpdateCheck), npos.Copy(), this.spreadDelay);
				}
			}
		}

		private Block GetReplacementBlock(Block neighborBlock, IWorldAccessor world)
		{
			AssetLocation replacementLocation = this.liquidFlowingCollisionReplacement;
			if (this.IsLiquidSourceBlock(neighborBlock))
			{
				replacementLocation = this.liquidSourceCollisionReplacement;
			}
			if (!(replacementLocation == null))
			{
				return world.GetBlock(replacementLocation);
			}
			return null;
		}

		private bool IsDifferentCollidableLiquid(Block block, Block other)
		{
			return other.IsLiquid() && block.IsLiquid() && other.LiquidCode == this.collidesWith;
		}

		private bool IsSameLiquid(Block block, Block other)
		{
			return block.LiquidCode == other.LiquidCode;
		}

		private bool IsLiquidSourceBlock(Block block)
		{
			return block.LiquidLevel == 7;
		}

		private bool TryLoweringLiquidLevel(Block ourBlock, IWorldAccessor world, BlockPos pos)
		{
			if (!this.IsLiquidSourceBlock(ourBlock) && this.GetMaxNeighbourLiquidLevel(ourBlock, world, pos) <= ourBlock.LiquidLevel)
			{
				this.LowerLiquidLevelAndNotifyNeighbors(ourBlock, pos, world);
				return true;
			}
			return false;
		}

		private void LowerLiquidLevelAndNotifyNeighbors(Block block, BlockPos pos, IWorldAccessor world)
		{
			this.SpreadLiquid(this.GetLessLiquidBlockId(world, pos, block), pos, world);
			BlockPos npos = pos.Copy();
			for (int i = 0; i < 6; i++)
			{
				BlockFacing.ALLFACES[i].IterateThruFacingOffsets(npos);
				Block liquidBlock = world.BlockAccessor.GetBlock(npos, 2);
				if (liquidBlock.BlockId != 0)
				{
					liquidBlock.OnNeighbourBlockChange(world, npos, pos);
				}
			}
		}

		private void TrySpreadIntoBlock(Block ourblock, Block ourSolid, BlockPos pos, BlockPos npos, BlockFacing facing, IWorldAccessor world)
		{
			if (this.CanSpreadIntoBlock(ourblock, ourSolid, pos, npos, facing, world))
			{
				Block neighborLiquid = world.BlockAccessor.GetBlock(npos, 2);
				if (this.IsDifferentCollidableLiquid(ourblock, neighborLiquid))
				{
					this.ReplaceLiquidBlock(neighborLiquid, npos, world);
					return;
				}
				this.SpreadLiquid(this.GetLessLiquidBlockId(world, npos, ourblock), npos, world);
			}
		}

		public int GetLessLiquidBlockId(IWorldAccessor world, BlockPos pos, Block block)
		{
			return this.GetLiquidBlockId(world, pos, block, block.LiquidLevel - 1);
		}

		public int GetMoreLiquidBlockId(IWorldAccessor world, BlockPos pos, Block block)
		{
			return this.GetLiquidBlockId(world, pos, block, Math.Min(7, block.LiquidLevel + 1));
		}

		public int GetLiquidBlockId(IWorldAccessor world, BlockPos pos, Block block, int liquidLevel)
		{
			if (liquidLevel < 1)
			{
				return 0;
			}
			Vec3i dir = new Vec3i();
			bool anySideFree = false;
			BlockPos npos = pos.Copy();
			IBlockAccessor blockAccessor = world.BlockAccessor;
			foreach (Cardinal val in Cardinal.ALL)
			{
				npos.Set(pos.X + val.Normali.X, pos.Y, pos.Z + val.Normali.Z);
				Block nblock = blockAccessor.GetBlock(npos, 2);
				if (nblock.LiquidLevel != liquidLevel && nblock.Replaceable >= 6000 && nblock.IsLiquid())
				{
					Vec3i normal = ((nblock.LiquidLevel < liquidLevel) ? val.Normali : val.Opposite.Normali);
					if (!val.IsDiagnoal)
					{
						nblock = blockAccessor.GetBlock(npos, 1);
						anySideFree |= !nblock.SideIsSolid(blockAccessor, npos, val.Opposite.Index / 2);
					}
					dir.X += normal.X;
					dir.Z += normal.Z;
				}
			}
			if (Math.Abs(dir.X) > Math.Abs(dir.Z))
			{
				dir.Z = 0;
			}
			else if (Math.Abs(dir.Z) > Math.Abs(dir.X))
			{
				dir.X = 0;
			}
			dir.X = Math.Sign(dir.X);
			dir.Z = Math.Sign(dir.Z);
			Cardinal flowDir = Cardinal.FromNormali(dir);
			if (flowDir != null)
			{
				return world.GetBlock(block.CodeWithParts(new string[]
				{
					flowDir.Initial,
					liquidLevel.ToString() ?? ""
				})).BlockId;
			}
			pos.Y--;
			Block downBlock = blockAccessor.GetBlock(pos, 2);
			pos.Y += 2;
			Block upBlock = blockAccessor.GetBlock(pos, 2);
			pos.Y--;
			bool flag = this.IsSameLiquid(downBlock, block);
			bool upLiquid = this.IsSameLiquid(upBlock, block);
			if ((flag && downBlock.Variant["flow"] == "d") || (upLiquid && upBlock.Variant["flow"] == "d"))
			{
				return world.GetBlock(block.CodeWithParts(new string[]
				{
					"d",
					liquidLevel.ToString() ?? ""
				})).BlockId;
			}
			if (anySideFree)
			{
				return world.GetBlock(block.CodeWithParts(new string[]
				{
					"d",
					liquidLevel.ToString() ?? ""
				})).BlockId;
			}
			return world.GetBlock(block.CodeWithParts(new string[]
			{
				"still",
				liquidLevel.ToString() ?? ""
			})).BlockId;
		}

		private int GetFallingLiquidBlockId(Block ourBlock, IWorldAccessor world)
		{
			return world.GetBlock(ourBlock.CodeWithParts(new string[] { "d", "6" })).BlockId;
		}

		public int GetMaxNeighbourLiquidLevel(Block ourblock, IWorldAccessor world, BlockPos pos)
		{
			Block ourSolid = world.BlockAccessor.GetBlock(pos, 1);
			BlockPos npos = pos.Copy();
			npos.Y++;
			Block ublock = world.BlockAccessor.GetBlock(npos, 2);
			npos.Y--;
			if (this.IsSameLiquid(ourblock, ublock) && (double)ourSolid.GetLiquidBarrierHeightOnSide(BlockFacing.UP, pos) == 0.0)
			{
				return 7;
			}
			int level = 0;
			for (int i = 0; i < BlockFacing.HORIZONTALS.Length; i++)
			{
				BlockFacing.HORIZONTALS[i].IterateThruFacingOffsets(npos);
				Block nblock = world.BlockAccessor.GetBlock(npos, 2);
				if (this.IsSameLiquid(ourblock, nblock))
				{
					int nLevel = nblock.LiquidLevel;
					if (ourSolid.GetLiquidBarrierHeightOnSide(BlockFacing.HORIZONTALS[i], pos) < (float)nLevel / 7f && world.BlockAccessor.GetBlock(npos, 1).GetLiquidBarrierHeightOnSide(BlockFacing.HORIZONTALS[i].Opposite, npos) < (float)nLevel / 7f)
					{
						level = Math.Max(level, nLevel);
					}
				}
			}
			return level;
		}

		public override bool IsReplacableBy(Block byBlock, ref EnumHandling handled)
		{
			handled = EnumHandling.PreventDefault;
			return (this.block.IsLiquid() || this.block.Replaceable >= BlockBehaviorTrueFiniteSpreadingLiquid.ReplacableThreshold) && byBlock.Replaceable <= this.block.Replaceable;
		}

		public List<PosAndDist> FindDownwardPaths(IWorldAccessor world, BlockPos pos, Block ourBlock)
		{
			List<PosAndDist> paths = new List<PosAndDist>();
			Queue<BlockPos> uncheckedPositions = new Queue<BlockPos>();
			int shortestPath = 99;
			BlockPos npos = new BlockPos();
			for (int i = 0; i < BlockBehaviorTrueFiniteSpreadingLiquid.downPaths.Length; i++)
			{
				Vec2i offset = BlockBehaviorTrueFiniteSpreadingLiquid.downPaths[i];
				npos.Set(pos.X + offset.X, pos.Y - 1, pos.Z + offset.Y);
				Block block = world.BlockAccessor.GetBlock(npos);
				npos.Y++;
				Block block2 = world.BlockAccessor.GetBlock(npos, 2);
				Block aboveblock = world.BlockAccessor.GetBlock(npos, 1);
				if (block2.LiquidLevel < ourBlock.LiquidLevel && block.Replaceable >= BlockBehaviorTrueFiniteSpreadingLiquid.ReplacableThreshold && aboveblock.Replaceable >= BlockBehaviorTrueFiniteSpreadingLiquid.ReplacableThreshold)
				{
					uncheckedPositions.Enqueue(new BlockPos(pos.X + offset.X, pos.Y, pos.Z + offset.Y));
					BlockPos foundPos = this.BfsSearchPath(world, uncheckedPositions, pos, ourBlock);
					if (foundPos != null)
					{
						PosAndDist pad = new PosAndDist
						{
							pos = foundPos,
							dist = pos.ManhattenDistance(pos.X + offset.X, pos.Y, pos.Z + offset.Y)
						};
						if (pad.dist == 1 && ourBlock.LiquidLevel < 7)
						{
							paths.Clear();
							paths.Add(pad);
							return paths;
						}
						paths.Add(pad);
						shortestPath = Math.Min(shortestPath, pad.dist);
					}
				}
			}
			for (int j = 0; j < paths.Count; j++)
			{
				if (paths[j].dist > shortestPath)
				{
					paths.RemoveAt(j);
					j--;
				}
			}
			return paths;
		}

		private BlockPos BfsSearchPath(IWorldAccessor world, Queue<BlockPos> uncheckedPositions, BlockPos target, Block ourBlock)
		{
			BlockPos npos = new BlockPos();
			BlockPos origin = null;
			while (uncheckedPositions.Count > 0)
			{
				BlockPos pos = uncheckedPositions.Dequeue();
				if (origin == null)
				{
					origin = pos;
				}
				int curDist = pos.ManhattenDistance(target);
				npos.Set(pos);
				for (int i = 0; i < BlockFacing.HORIZONTALS.Length; i++)
				{
					BlockFacing.HORIZONTALS[i].IterateThruFacingOffsets(npos);
					if (npos.ManhattenDistance(target) <= curDist)
					{
						if (npos.Equals(target))
						{
							return pos;
						}
						if (world.BlockAccessor.GetMostSolidBlock(npos.X, npos.Y, npos.Z).GetLiquidBarrierHeightOnSide(BlockFacing.HORIZONTALS[i].Opposite, npos) < (float)(ourBlock.LiquidLevel - pos.ManhattenDistance(origin)) / 7f)
						{
							uncheckedPositions.Enqueue(npos.Copy());
						}
					}
				}
			}
			return null;
		}

		public override bool ShouldReceiveClientParticleTicks(IWorldAccessor world, IPlayer byPlayer, BlockPos pos, ref EnumHandling handled)
		{
			handled = EnumHandling.PreventDefault;
			if (this.block.ParticleProperties == null || this.block.ParticleProperties.Length == 0)
			{
				return false;
			}
			if (this.block.LiquidCode == "lava")
			{
				return world.BlockAccessor.GetBlock(pos.X, pos.Y + 1, pos.Z).Replaceable > BlockBehaviorTrueFiniteSpreadingLiquid.ReplacableThreshold;
			}
			handled = EnumHandling.PassThrough;
			return false;
		}

		private static AssetLocation CreateAssetLocation(JsonObject properties, string propertyName)
		{
			return BlockBehaviorTrueFiniteSpreadingLiquid.CreateAssetLocation(properties, null, propertyName);
		}

		private static AssetLocation CreateAssetLocation(JsonObject properties, string prefix, string propertyName)
		{
			JsonObject jsonObject = properties[propertyName];
			string value = ((jsonObject != null) ? jsonObject.AsString(null) : null);
			if (value == null)
			{
				return null;
			}
			if (prefix != null)
			{
				return new AssetLocation(prefix + value);
			}
			return new AssetLocation(value);
		}

		private const int MAXLEVEL = 7;

		private const float MAXLEVEL_float = 7f;

		public static Vec2i[] downPaths = ShapeUtil.GetSquarePointsSortedByMDist(3);

		public static SimpleParticleProperties steamParticles;

		public static int ReplacableThreshold = 5000;

		private AssetLocation collisionReplaceSound;

		private int spreadDelay = 50;

		private string collidesWith;
		
		private AssetLocation liquidSourceCollisionReplacement;
		
		private AssetLocation liquidFlowingCollisionReplacement;
	}
}