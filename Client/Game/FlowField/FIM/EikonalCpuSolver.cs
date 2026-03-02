using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using SpacetimeDB.Game.FlowField.Tile;

namespace SpacetimeDB.Game.FlowField.FIM;

public sealed class EikonalCpuSolver
{
	private const float Sqrt2 = 1.41421356237f;

	public IntegrationField Solve(
		CostField costField,
		IReadOnlyList<Seed> seeds,
		FimConfig config,
		bool[]? losMask = null,
		int[]? allowedTiles = null)
	{
		ArgumentNullException.ThrowIfNull(costField);
		ArgumentNullException.ThrowIfNull(seeds);
		ArgumentNullException.ThrowIfNull(config);

		config.Validate();

		int width = costField.Width;
		int height = costField.Height;
		int cellCount = width * height;

		if (losMask is not null && losMask.Length != cellCount)
		{
			throw new ArgumentException("LOS mask length must match the cost field size.", nameof(losMask));
		}

		bool useTiledSolver = config.EnableTiledProcessing;

		int tileSize = useTiledSolver ? Math.Max(1, config.TileSize) : 1;
		int tilesX = (width + tileSize - 1) / tileSize;
		int tilesY = (height + tileSize - 1) / tileSize;
		int tileCount = tilesX * tilesY;

		int paddedWidth = width + 2;
		int paddedHeight = height + 2;
		int paddedCellCount = paddedWidth * paddedHeight;

		float[] solution = new float[paddedCellCount];
		Array.Fill(solution, config.InfinityValue);

		float[] slowness = new float[paddedCellCount];
		ComputeSlowness(costField, losMask, config, slowness, paddedWidth, tileSize, tilesX, tilesY, allowedTiles);

		bool[] activeTiles = new bool[tileCount];
		bool[] neighborEnqueue = new bool[tileCount];
		List<int> activeTileList = new(tileCount);
		List<int> neighborTileList = new(tileCount);

		bool hasSeed = SeedInitialWavefront(costField, seeds, solution, slowness, config, paddedWidth, activeTiles, activeTileList, allowedTiles);

		if (!hasSeed)
		{
			return new IntegrationField(width, height);
		}

		ParallelOptions parallelOptions = new();
		if (config.MaxDegreeOfParallelism is int maxDegree && maxDegree > 0)
		{
			parallelOptions.MaxDegreeOfParallelism = maxDegree;
		}

		int iterations = 0;                                                             
		bool allowParallel = config.AllowParallel;
		int innerIterations = Math.Max(1, config.ItersPerSweep);
		int neighborIterations = Math.Max(1, config.NeighborItersPerSweep);
		if (!useTiledSolver)
		{
			innerIterations = 1;
			neighborIterations = 1;
		}
		float epsilon = config.Epsilon;
		float infinity = config.InfinityValue;
		bool useDiagonal = config.UseDiagonalStencil;
		bool enqueueDiagonalTiles = config.EnqueueDiagonalNeighborTiles;
		int parallelTileThreshold = config.ParallelTileThreshold;

		while (activeTileList.Count > 0)
		{
			iterations++;

			bool changed = ProcessTiles(
				activeTileList,
				innerIterations,
				tileSize,
				tilesX,
				tilesY,
				width,
				height,
				paddedWidth,
				paddedHeight,
				solution,
				slowness,
				epsilon,
				infinity,
				useDiagonal,
				enqueueNeighbors: true,
				activeTiles,
				neighborEnqueue,
				parallelOptions,
				allowParallel,
				parallelTileThreshold,
				enqueueDiagonalTiles,
				allowedTiles
			);

			BuildNeighborTileList(neighborEnqueue, neighborTileList, allowedTiles);

			if (neighborTileList.Count > 0)
			{
				bool neighborChanged = ProcessTiles(
					neighborTileList,
					iterationCount: neighborIterations,
					tileSize,
					tilesX,
					tilesY,
					width,
					height,
					paddedWidth,
					paddedHeight,
					solution,
					slowness,
					epsilon,
					infinity,
					useDiagonal,
					enqueueNeighbors: false,
					activeTiles,
					neighborEnqueue,
					parallelOptions,
					allowParallel,
					parallelTileThreshold,
					enqueueDiagonalTiles,
					allowedTiles
				);

				changed |= neighborChanged;
			}

			RebuildActiveTileList(activeTiles, activeTileList, allowedTiles);

			if (!changed)
			{
				break;
			}
		}

		IntegrationField field = new(width, height);
		PopulateOutput(solution, costField, losMask, config, paddedWidth, tileSize, tilesX, tilesY, allowedTiles, field);
		return field;
	}

	private static void ComputeSlowness(
		CostField costField,
		bool[]? losMask,
		FimConfig config,
		float[] slowness,
		int paddedWidth,
		int tileSize,
		int tilesX,
		int tilesY,
		int[]? allowedTiles)
	{
		Array.Fill(slowness, float.PositiveInfinity);

		int width = costField.Width;
		int height = costField.Height;
		byte[] costs = costField.Costs;
		bool[] pathable = costField.PathableMask;
		float scale = config.StepScale;
		float minimum = config.MinimumSlowness;

		if (allowedTiles is null)
		{
			for (int y = 0; y < height; y++)
			{
				int rowOffset = (y + 1) * paddedWidth;
				int costRowOffset = y * width;

				for (int x = 0; x < width; x++)
				{
					int costIndex = costRowOffset + x;
					if (!pathable[costIndex])
					{
						continue;
					}

					if (losMask is not null && !losMask[costIndex])
					{
						continue;
					}

					float baseValue = MathF.Max(minimum, costs[costIndex]);
					slowness[rowOffset + (x + 1)] = baseValue * scale;
				}
			}
			return;
		}

		var cells = FlowCell.GetCells(config, allowedTiles);
		foreach (var cell in cells)
		{
			// Calculate global cell coordinates from the pre-calculated index
			int globalX = cell.Index % width;
			int globalY = cell.Index / width;
			
			int rowOffset = (globalY + 1) * paddedWidth;
			int costRowOffset = globalY * width;
			int costIndex = costRowOffset + globalX;
			
			if (!pathable[costIndex])
			{
				continue;
			}
		
			if (losMask is not null && !losMask[costIndex])
			{
				continue;
			}
		
			float baseValue = MathF.Max(minimum, costs[costIndex]);
			slowness[rowOffset + (globalX + 1)] = baseValue * scale;
		}
	}

	private static bool SeedInitialWavefront(
		CostField costField,
		IReadOnlyList<Seed> seeds,
		float[] solution,
		float[] slowness,
		FimConfig config,
		int paddedWidth,
		bool[] activeTiles,
		List<int> activeTileList,
		int[]? allowedTiles)
	{
		bool hasSeed = false;
		int width = costField.Width;
		int height = costField.Height;

		foreach (Seed seed in seeds)
		{
			if ((uint)seed.X >= (uint)width || (uint)seed.Y >= (uint)height)
			{
				continue;
			}

			int paddedIndex = GetPaddedIndex(seed.X, seed.Y, paddedWidth);
			if (float.IsPositiveInfinity(slowness[paddedIndex]))
			{
				continue;
			}

			solution[paddedIndex] = MathF.Min(solution[paddedIndex], seed.InitialCost);
			int tileIndex = FlowTile.TileIndexToFlatIndex(FlowTile.CellToTileIndex(new Vector2I(seed.X, seed.Y), config), config);
			
			if (allowedTiles is not null && !allowedTiles.Contains(tileIndex))
			{
				continue;
			}
			ActivateTile(tileIndex, activeTiles, activeTileList);
			hasSeed = true;
		}

		return hasSeed;
	}
	
	private static bool ProcessTiles(
		List<int> tiles,
		int iterationCount,
		int tileSize,
		int tilesX,
		int tilesY,
		int width,
		int height,
		int paddedWidth,
		int paddedHeight,
		float[] solution,
		float[] slowness,
		float epsilon,
		float infinity,
		bool useDiagonal,
		bool enqueueNeighbors,
		bool[] activeTiles,
		bool[] neighborEnqueue,
		ParallelOptions parallelOptions,
		bool allowParallel,
		int parallelTileThreshold,
		bool enqueueDiagonalTiles,
		int[]? allowedTiles)
	{
		if (tiles.Count == 0)
		{
			return false;
		}

		int stride = tileSize + 2;
		int bufferLength = stride * stride;
		int changedFlag = 0;

		iterationCount = Math.Max(1, iterationCount);
		if (allowParallel && ShouldParallelizeTiles(tiles.Count, parallelOptions, parallelTileThreshold))
		{
			Parallel.ForEach(tiles, parallelOptions,
				() => ArrayPool<float>.Shared.Rent(bufferLength),
				(tileIndex, loopState, buffer) =>
				{
					TileProcessOutcome outcome = ProcessTile(
						tileIndex,
						iterationCount,
						tileSize,
						tilesX,
						tilesY,
						width,
						height,
						paddedWidth,
						paddedHeight,
						solution,
						slowness,
						epsilon,
						infinity,
						useDiagonal,
						buffer);

					if (outcome.Changed)
					{
						Interlocked.Exchange(ref changedFlag, 1);
					}

					UpdateTileState(
						tileIndex,
						outcome,
						tilesX,
						tilesY,
						enqueueNeighbors,
						activeTiles,
						neighborEnqueue,
						useDiagonal,
						enqueueDiagonalTiles,
						allowedTiles
					);

					return buffer;
				},
				buffer => ArrayPool<float>.Shared.Return(buffer));
		}
		else
		{
			float[] buffer = ArrayPool<float>.Shared.Rent(bufferLength);
			try
			{
				for (int i = 0; i < tiles.Count; i++)
				{
					int tileIndex = tiles[i];
					if (allowedTiles is not null && !allowedTiles.Contains(tileIndex))
					{
						continue;
					}

					TileProcessOutcome outcome = ProcessTile(
						tileIndex,
						iterationCount,
						tileSize,
						tilesX,
						tilesY,
						width,
						height,
						paddedWidth,
						paddedHeight,
						solution,
						slowness,
						epsilon,
						infinity,
						useDiagonal,
						buffer);

					if (outcome.Changed)
					{
						changedFlag = 1;
					}

					UpdateTileState(
						tileIndex,
						outcome,
						tilesX,
						tilesY,
						enqueueNeighbors,
						activeTiles,
						neighborEnqueue,
						useDiagonal,
						enqueueDiagonalTiles,
						allowedTiles);
				}
			}
			finally
			{
				ArrayPool<float>.Shared.Return(buffer);
			}
		}

		return changedFlag != 0;
	}

	private static TileProcessOutcome ProcessTile(
		int tileIndex,
		int iterationCount,
		int tileSize,
		int tilesX,
		int tilesY,
		int width,
		int height,
		int paddedWidth,
		int paddedHeight,
		float[] solution,
		float[] slowness,
		float epsilon,
		float infinity,
		bool useDiagonal,
		float[] buffer)
	{
		Array.Fill(buffer, infinity);

		int tileX = tileIndex % tilesX;
		int tileY = tileIndex / tilesX;
		int startX = tileX * tileSize;
		int startY = tileY * tileSize;

		int tileWidth = Math.Min(tileSize, Math.Max(0, width - startX));
		int tileHeight = Math.Min(tileSize, Math.Max(0, height - startY));

		if (tileWidth <= 0 || tileHeight <= 0)
		{
			return new TileProcessOutcome(false, true, false);
		}

		int stride = tileSize + 2;

		for (int localY = -1; localY <= tileHeight; localY++)
		{
			int globalY = startY + localY;
			int paddedY = globalY + 1;
			if ((uint)paddedY >= (uint)paddedHeight)
			{
				continue;
			}

			int bufferRow = (localY + 1) * stride;

			for (int localX = -1; localX <= tileWidth; localX++)
			{
				int globalX = startX + localX;
				int paddedX = globalX + 1;

				if ((uint)paddedX >= (uint)paddedWidth)
				{
					continue;
				}

				buffer[bufferRow + (localX + 1)] = solution[paddedY * paddedWidth + paddedX];
			}
		}

		bool changed = false;
		bool boundaryChanged = false;
		float maxResidual = 0f;

		for (int iter = 0; iter < iterationCount; iter++)
		{
			bool updatedThisIteration = false;

			for (int localY = 0; localY < tileHeight; localY++)
			{
				int globalY = startY + localY;
				int paddedY = globalY + 1;
				int bufferRow = (localY + 1) * stride;

				for (int localX = 0; localX < tileWidth; localX++)
				{
					int globalX = startX + localX;
					int paddedIndex = paddedY * paddedWidth + (globalX + 1);
					float s = slowness[paddedIndex];

					if (float.IsPositiveInfinity(s))
					{
						continue;
					}

					int center = bufferRow + (localX + 1);
					float current = buffer[center];
					float left = buffer[center - 1];
					float right = buffer[center + 1];
					float up = buffer[center - stride];
					float down = buffer[center + stride];

					float candidate = SolveEikonal(MathF.Min(left, right), MathF.Min(up, down), s);

					if (useDiagonal)
					{
						float nw = buffer[center - stride - 1];
						float ne = buffer[center - stride + 1];
						float sw = buffer[center + stride - 1];
						float se = buffer[center + stride + 1];
						float diagBase = MathF.Min(MathF.Min(nw, ne), MathF.Min(sw, se));

						if (!float.IsPositiveInfinity(diagBase))
						{
							float diagonal = diagBase + s * Sqrt2;
							if (diagonal + epsilon < candidate)
							{
								candidate = diagonal;
							}
						}
					}

					float nextValue = MathF.Min(current, candidate);
					float delta = current - nextValue;

					if (delta > 0f)
					{
						buffer[center] = nextValue;
						changed = true;
						updatedThisIteration = true;
						maxResidual = MathF.Max(maxResidual, delta);

						if (localX == 0 || localX == tileWidth - 1 || localY == 0 || localY == tileHeight - 1)
						{
							boundaryChanged = true;
						}
					}
				}
			}

			if (!updatedThisIteration)
			{
				break;
			}
		}

		for (int localY = 0; localY < tileHeight; localY++)
		{
			int globalY = startY + localY;
			int paddedY = (globalY + 1) * paddedWidth;
			int bufferRow = (localY + 1) * stride;

			for (int localX = 0; localX < tileWidth; localX++)
			{
				int globalX = startX + localX;
				solution[paddedY + (globalX + 1)] = buffer[bufferRow + (localX + 1)];
			}
		}

		bool converged = maxResidual <= epsilon;
		return new TileProcessOutcome(changed, converged, boundaryChanged && changed);
	}

	private static void UpdateTileState(
		int tileIndex,
		TileProcessOutcome outcome,
		int tilesX,
		int tilesY,
		bool enqueueNeighbors,
		bool[] activeTiles,
		bool[] neighborEnqueue,
		bool useDiagonal,
		bool enqueueDiagonal,
		int[]? allowedTiles)
	{
		if (outcome.Converged)
		{
			activeTiles[tileIndex] = false;
			if (enqueueNeighbors)
			{
				EnqueueNeighborTiles(tileIndex, tilesX, tilesY, neighborEnqueue, useDiagonal, enqueueDiagonal, allowedTiles);
			}
		}
		else
		{
			activeTiles[tileIndex] = true;
			if (enqueueNeighbors && outcome.BoundaryChanged)
			{
				EnqueueNeighborTiles(tileIndex, tilesX, tilesY, neighborEnqueue, useDiagonal, enqueueDiagonal, allowedTiles);
			}
		}
	}

	private static void EnqueueNeighborTiles(int tileIndex, int tilesX, int tilesY, bool[] neighborEnqueue, bool useDiagonal, bool enqueueDiagonal, int[]? allowedTiles)
	{
		int tileX = tileIndex % tilesX;
		int tileY = tileIndex / tilesX;

		void Enqueue(int tx, int ty)
		{
			if ((uint)tx < (uint)tilesX && (uint)ty < (uint)tilesY)
			{
				int idx = ty * tilesX + tx;
				if (allowedTiles is null || allowedTiles.Contains(idx))
				{
					neighborEnqueue[idx] = true;
				}
			}
		}

		Enqueue(tileX - 1, tileY);
		Enqueue(tileX + 1, tileY);
		Enqueue(tileX, tileY - 1);
		Enqueue(tileX, tileY + 1);

		if (useDiagonal && enqueueDiagonal)
		{
			Enqueue(tileX - 1, tileY - 1);
			Enqueue(tileX - 1, tileY + 1);
			Enqueue(tileX + 1, tileY - 1);
			Enqueue(tileX + 1, tileY + 1);
		}
	}

	private static void RebuildActiveTileList(bool[] activeTiles, List<int> destination, int[]? allowedTiles = null)
	{
		destination.Clear();
		for (int i = 0; i < activeTiles.Length; i++)
		{
			if (!activeTiles[i])
			{
				continue;
			}

			if (allowedTiles is not null && !allowedTiles.Contains(i))
			{
				activeTiles[i] = false;
				continue;
			}

			destination.Add(i);
		}
	}

	private static void BuildNeighborTileList(bool[] neighborFlags, List<int> destination, int[]? allowedTiles = null)
	{
		destination.Clear();
		for (int i = 0; i < neighborFlags.Length; i++)
		{
			if (!neighborFlags[i])
			{
				continue;
			}

			if (allowedTiles is not null && !allowedTiles.Contains(i))
			{
				neighborFlags[i] = false;
				continue;
			}

			destination.Add(i);
			neighborFlags[i] = false;
		}
	}

	private static void PopulateOutput(
		float[] solution,
		CostField costField,
		bool[]? losMask,
		FimConfig config,
		int paddedWidth,
		int tileSize,
		int tilesX,
		int tilesY,
		int[]? allowedTiles,
		IntegrationField output)
	{
		int width = costField.Width;
		int height = costField.Height;
		float[] destCosts = output.Costs;
		byte[] destFlags = output.Flags;
		bool[] pathable = costField.PathableMask;
		float infinity = config.InfinityValue;

		if (allowedTiles is not null) {
			// TODO: This is a bit of a hack. Should work on making the rest of pipeline not setting all unused costs to infinity.
			Array.Fill(destCosts, infinity);
			
			foreach (var tileIndex in allowedTiles)
			{
				int tileX = tileIndex % tilesX;
				int tileY = tileIndex / tilesX;
				int startX = tileX * config.TileSize;
				int startY = tileY * config.TileSize;
				
				for (int localY = 0; localY < config.TileSize; localY++)
				{
					int globalY = startY + localY;
					int costRowOffset = globalY * width;
					int paddedRowOffset = (globalY + 1) * paddedWidth;

					for (int localX = 0; localX < config.TileSize; localX++)
					{
						int globalX = startX + localX;
						int costIndex = costRowOffset + globalX;
						
						float cost = solution[paddedRowOffset + (globalX + 1)];
						if (cost > infinity)
						{
							cost = infinity;
						}
						
						FlowFlags.IntegrationFlags flags = FlowFlags.IntegrationFlags.None;
						bool isPathable = pathable[costIndex];
						if (isPathable)
						{
							flags |= FlowFlags.IntegrationFlags.Pathable;
							
							if (losMask is null || losMask[costIndex])
							{
								flags |= FlowFlags.IntegrationFlags.LineOfSight;
							}
						}
						
						destCosts[costIndex] = cost;
						destFlags[costIndex] = (byte)flags;
					}
				}
			}   
			return;
		}

		for (int y = 0; y < height; y++)
		{
			int paddedRowOffset = (y + 1) * paddedWidth;
			int rowOffset = y * width;

			for (int x = 0; x < width; x++)
			{
				int index = rowOffset + x;
				float cost = solution[paddedRowOffset + (x + 1)];
				if (cost > infinity)
				{
					cost = infinity;
				}

				FlowFlags.IntegrationFlags flags = FlowFlags.IntegrationFlags.None;
				bool isPathable = pathable[index];
				if (isPathable)
				{
					flags |= FlowFlags.IntegrationFlags.Pathable;

					if (losMask is null || losMask[index])
					{
						flags |= FlowFlags.IntegrationFlags.LineOfSight;
					}
				}

				destCosts[index] = cost;
				destFlags[index] = (byte)flags;
			}
		}
	}

	// private static int GetTileIndex(int x, int y, int tileSize, int tilesX)
	// {
	//     int tileX = x / tileSize;
	//     int tileY = y / tileSize;
	//     return tileY * tilesX + tileX;
	// }

	private static int GetPaddedIndex(int x, int y, int paddedWidth)
	{
		return (y + 1) * paddedWidth + (x + 1);
	}

	private static void ActivateTile(int tileIndex, bool[] activeTiles, List<int> activeTileList)
	{
		if ((uint)tileIndex >= (uint)activeTiles.Length)
		{
			return;
		}

		if (!activeTiles[tileIndex])
		{
			activeTiles[tileIndex] = true;
			activeTileList.Add(tileIndex);
		}
	}

	private static float SolveEikonal(float a, float b, float s)
	{
		bool aInf = float.IsPositiveInfinity(a);
		bool bInf = float.IsPositiveInfinity(b);

		if (aInf && bInf)
		{
			return float.PositiveInfinity;
		}

		if (aInf)
		{
			return b + s;
		}

		if (bInf)
		{
			return a + s;
		}

		float min = MathF.Min(a, b);
		float max = MathF.Max(a, b);
		float diff = max - min;

		if (diff >= s)
		{
			return min + s;
		}

		float disc = 2f * s * s - diff * diff;
		if (disc <= 0f)
		{
			return min + s;
		}

		return 0.5f * (min + max + MathF.Sqrt(disc));
	}

	private static bool ShouldParallelizeTiles(int activeCount, ParallelOptions options, int threshold)
	{
		if (threshold <= 0)
		{
			return true;
		}

		if (activeCount < threshold)
		{
			return false;
		}

		if (options.MaxDegreeOfParallelism == 1)
		{
			return false;
		}

		return true;
	}

	private readonly record struct TileProcessOutcome(bool Changed, bool Converged, bool BoundaryChanged);

	public readonly record struct Seed(int X, int Y, float InitialCost);

	private static double MeasureMs(string label, Action action)
	{
		ulong start = Time.GetTicksUsec();
		action();
		ulong end = Time.GetTicksUsec();
		double ms = (end - start) / 1000.0;
		GD.Print($"[EikonalCpuSolver] {label} took {ms:F3} ms");
		return ms;
	}
}
