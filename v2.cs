using System;
using System.Collections.Generic;
using System.Linq;
using Chess;

namespace Sharp
{
    class Program
    {
        static ChessBoard board = new ChessBoard();
        static int searchDepth = 0;
        static int defaultSearchDepth = 6;
        static int endgameExtentedDepth = 1;
        static float extentedDepthPhase = 0.8f;
        static int moveTime = 0;
        static DateTime searchStart;
        static long nodeCount = 0;
        static List<Move> principalVariation = new List<Move>();
        static List<Move> lastPv = new List<Move>();

        static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.ASCII;

            while (true)
            {
                string? line = Console.ReadLine();
                if (line == null) continue;

                if (line == "uci")
                {
                    Console.WriteLine("id name Sharp");
                    Console.WriteLine("id author Sandeep Singh");
                    Console.WriteLine("uciok");
                }
                else if (line == "isready")
                {
                    Console.WriteLine("readyok");
                }
                else if (line.StartsWith("position"))
                {
                    ParsePosition(line);
                }
                else if (line.StartsWith("go"))
                {
                    ParseGo(line);
                    var best = FindBestMove();
                    Console.WriteLine($"bestmove {best}");
                }
                else if (line == "ucinewgame")
                {
                    board = new ChessBoard();
                    lastPv.Clear();
                }
                else if (line == "quit")
                {
                    break;
                }
            }
        }

        static Move? LanToMove(string lan)
        {
            var moves = board.Moves();
            foreach (var move in moves)
            {
                if (MoveToUci(move) == lan)
                    return move;
            }
            return null;
        }

        static Move? UciToMove(string uci)
        {
            var moves = board.Moves();
            foreach (var move in moves)
            {
                if (MoveToUci(move) == uci)
                    return move;
            }
            return null;
        }

        static void ParsePosition(string line)
        {
            var parts = line.Split(' ');
            int idx = 1;

            board = new ChessBoard();

            if (parts[idx] == "startpos")
            {
                idx++;
            }
            else if (parts[idx] == "fen")
            {
                string fen = string.Join(" ", parts.Skip(idx + 1).Take(6));
                board = ChessBoard.LoadFromFen(fen);
                idx += 7;
            }

            if (idx < parts.Length && parts[idx] == "moves")
            {
                idx++;
                while (idx < parts.Length)
                {
                    string lanMove = parts[idx];
                    var move = LanToMove(lanMove);
                    if (move != null)
                    {
                        board.Move(move);
                    }
                    idx++;
                }
            }
        }

        static void ParseGo(string line)
        {
            var parts = line.Split(' ');
            moveTime = 0;
            searchDepth = defaultSearchDepth;
            if (GetGamePhase() >= extentedDepthPhase)
            {
                searchDepth = defaultSearchDepth + endgameExtentedDepth;
            }

            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "depth" && i + 1 < parts.Length)
                    searchDepth = int.Parse(parts[i + 1]);
                else if (parts[i] == "movetime" && i + 1 < parts.Length)
                    moveTime = int.Parse(parts[i + 1]);
            }
        }

        static string FindBestMove()
        {
            searchStart = DateTime.Now;
            nodeCount = 0;
            Move bestMove = board.Moves()[0];

            if (moveTime > 0)
            {
                for (int depth = 1; depth <= 256; depth++)
                {
                    principalVariation.Clear();
                    int alpha = int.MinValue + 1;
                    int beta = int.MaxValue;
                    int bestScore = int.MinValue;

                    var legalMoves = OrderMoves(board.Moves().ToArray());
                    foreach (var move in legalMoves)
                    {
                        if (TimeExpired())
                            break;

                        board.Move(move);
                        principalVariation.Clear();
                        int score = -PVS(depth - 1, -beta, -alpha, principalVariation);
                        board.Cancel();

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestMove = move;
                        }

                        if (score > alpha)
                            alpha = score;
                    }

                    if (TimeExpired())
                        break;

                    long elapsed = (long)(DateTime.Now - searchStart).TotalMilliseconds;
                    double nps = elapsed > 0 ? (nodeCount * 1000.0) / elapsed : 0;

                    var pvMoves = new List<Move> { bestMove };
                    pvMoves.AddRange(principalVariation.Take(depth - 1));
                    var pvString = string.Join(" ", pvMoves.Select(MoveToUci));
                    Console.WriteLine($"info depth {depth} nodes {nodeCount} score cp {bestScore} pv {pvString} time {elapsed} nps {(long)nps}");

                    if (TimeExpired())
                        break;
                }
            }
            else
            {
                principalVariation.Clear();
                int alpha = int.MinValue + 1;
                int beta = int.MaxValue;
                int bestScore = int.MinValue;

                var legalMoves = OrderMoves(board.Moves().ToArray());
                foreach (var move in legalMoves)
                {
                    board.Move(move);
                    principalVariation.Clear();
                    int score = -PVS(searchDepth - 1, -beta, -alpha, principalVariation);
                    board.Cancel();

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMove = move;
                    }

                    if (score > alpha)
                        alpha = score;
                }

                long elapsed = (long)(DateTime.Now - searchStart).TotalMilliseconds;
                double nps = elapsed > 0 ? (nodeCount * 1000.0) / elapsed : 0;

                var pvMoves = new List<Move> { bestMove };
                pvMoves.AddRange(principalVariation.Take(searchDepth - 1));
                var pvString = string.Join(" ", pvMoves.Select(MoveToUci));
                Console.WriteLine($"info depth {searchDepth} nodes {nodeCount} score cp {bestScore} pv {pvString} time {elapsed} nps {(long)nps}");
            }

            lastPv.Clear();
            lastPv.Add(bestMove);
            lastPv.AddRange(principalVariation.Take(Math.Min(principalVariation.Count, 5)));

            return MoveToUci(bestMove);
        }

        static bool TimeExpired()
        {
            return (DateTime.Now - searchStart).TotalMilliseconds >= moveTime;
        }

        static string MoveToUci(Move move)
        {
            string moveStr = move.ToString();
            var parts = moveStr.Split('-');
            if (parts.Length >= 3)
            {
                string from = parts[1].Trim();
                string to = parts[2].Trim().TrimEnd('}');

                // Check for promotion
                if (move.Promotion != null)
                {
                    char promotionChar = GetPromotionChar(move.Promotion.Type);
                    return $"{from}{to}{promotionChar}";
                }

                return $"{from}{to}";
            }
            return moveStr;
        }

        static char GetPromotionChar(PieceType type)
        {
            return type.Value switch
            {
                2 => 'r',  // Rook
                3 => 'n',  // Knight
                4 => 'b',  // Bishop
                5 => 'q',  // Queen
                _ => 'q',  // Default to queen
            };
        }

        // ===== PVS (PRINCIPAL VARIATION SEARCH / NEGASCOUT) =====
        static int PVS(int depth, int alpha, int beta, List<Move> pv)
        {
            nodeCount++;

            if (moveTime > 0 && TimeExpired())
                return Evaluate(depth);

            if (depth == 0)
                return Evaluate(depth);

            int best = int.MinValue;
            List<Move> bestPv = new List<Move>();

            var legalMoves = OrderMoves(board.Moves().ToArray());

            if (legalMoves.Length == 0)
            {
                bool inCheck = board.Turn == PieceColor.White ? board.WhiteKingChecked : board.BlackKingChecked;
                if (inCheck)
                {
                    return -(1000000 + (depth * 100));
                }
                return 0; // Stalemate
            }

            int moveCount = 0;
            foreach (var move in legalMoves)
            {
                if (moveTime > 0 && TimeExpired())
                    break;

                board.Move(move);
                List<Move> childPv = new List<Move>();
                int score;

                if (moveCount == 0)
                {
                    // First move: search with full window [alpha, beta]
                    score = -PVS(depth - 1, -beta, -alpha, childPv);
                }
                else
                {
                    int searchDepthForMove = depth - 1;

                    // ===== LATE MOVE REDUCTION (LMR) =====
                    // Reduce depth for moves that are unlikely to be best
                    if (depth >= 3 && moveCount >= 4 && !move.IsCheck && move.CapturedPiece == null && move.Promotion == null)
                    {
                        // LMR formula: reduce by 1 or 2 plies based on move count
                        int reduction = moveCount < 8 ? 1 : 2;
                        searchDepthForMove = Math.Max(0, depth - 1 - reduction);
                    }

                    // Search with null window [alpha, alpha+1] at reduced depth
                    score = -PVS(searchDepthForMove, -alpha - 1, -alpha, childPv);

                    // If LMR search indicates this move might be better, re-search with full window
                    if (score > alpha && score < beta)
                    {
                        childPv.Clear();
                        score = -PVS(depth - 1, -beta, -score, childPv);
                    }
                }

                board.Cancel();

                if (score > best)
                {
                    best = score;
                    bestPv = new List<Move> { move };
                    bestPv.AddRange(childPv);
                }

                if (best > alpha)
                    alpha = best;

                if (alpha >= beta)
                    break;

                moveCount++;
            }

            pv.AddRange(bestPv);
            return best;
        }

        static Move[] OrderMoves(Move[] moves)
        {
            int PieceValue(PieceType type) => type.Value switch
            {
                1 => 100,
                2 => 500,
                3 => 320,
                4 => 330,
                5 => 900,
                6 => 20000,
                _ => 0,
            };

            int n = moves.Length;
            int[] scores = new int[n];

            Move? pvMove = lastPv.Count > 0 ? lastPv[0] : null;

            for (int i = 0; i < n; i++)
            {
                var move = moves[i];
                int score = 0;

                // ===== TIER 1: PV MOVE =====
                if (pvMove != null && MovesEqual(move, pvMove))
                {
                    scores[i] = 10_000_000;
                    continue;
                }

                // ===== TIER 1.5: CASTLING =====
                if (move.IsCastling)
                {
                    scores[i] = 5_000_000;
                    continue;
                }

                // ===== TIER 2: CAPTURES (MVV-LVA) =====
                if (move.CapturedPiece != null)
                {
                    int victim = PieceValue(move.CapturedPiece.Type);
                    int attacker = PieceValue(move.Piece.Type);

                    score = 1_000_000 + (victim * 10) - attacker;

                    if (move.Promotion != null)
                        score += 500_000;
                }
                else
                {
                    // ===== TIER 3: PROMOTIONS =====
                    if (move.Promotion != null)
                    {
                        score = 750_000 + PieceValue(move.Promotion.Type) * 10;
                    }
                    // ===== TIER 4: CHECKS =====
                    else if (move.IsCheck)
                    {
                        score = 500_000;
                    }
                }

                scores[i] = score;
            }

            int topCount = Math.Min(30, moves.Length);

            for (int i = 0; i < topCount; i++)
            {
                // Find the move with the highest score from i to end
                int bestIndex = i;
                for (int j = i + 1; j < moves.Length; j++)
                {
                    if (scores[j] > scores[bestIndex])
                        bestIndex = j;
                }

                // Swap it into position i
                if (bestIndex != i)
                {
                    var tempMove = moves[i];
                    moves[i] = moves[bestIndex];
                    moves[bestIndex] = tempMove;

                    int tempScore = scores[i];
                    scores[i] = scores[bestIndex];
                    scores[bestIndex] = tempScore;
                }
            }

            return moves;
        }

        static bool MovesEqual(Move a, Move b)
        {
            if (a.OriginalPosition.X != b.OriginalPosition.X || a.OriginalPosition.Y != b.OriginalPosition.Y)
                return false;

            if (a.NewPosition.X != b.NewPosition.X || a.NewPosition.Y != b.NewPosition.Y)
                return false;

            // Compare promotion if any
            var aPromo = a.Promotion?.Type;
            var bPromo = b.Promotion?.Type;
            return aPromo == bPromo;
        }

        static int Evaluate(int searchDepth)
        {
            int score = 0;

            // Material evaluation - direct board access instead of ASCII
            for (int rank = 0; rank < 8; rank++)
            {
                for (int file = 0; file < 8; file++)
                {
                    var piece = board[file, rank];
                    if (piece == null) continue;

                    int pieceValue = 0;
                    if (piece.Type == PieceType.Pawn) pieceValue = 100;
                    else if (piece.Type == PieceType.Knight) pieceValue = 320;
                    else if (piece.Type == PieceType.Bishop) pieceValue = 330;
                    else if (piece.Type == PieceType.Rook) pieceValue = 500;
                    else if (piece.Type == PieceType.Queen) pieceValue = 900;

                    score += piece.Color == PieceColor.White ? pieceValue : -pieceValue;
                }
            }

            // Check bonus
            bool opponentInCheck = board.Turn == PieceColor.White
                ? board.BlackKingChecked
                : board.WhiteKingChecked;

            if (opponentInCheck)
                score += 50;

            // Game phase calculation without ASCII
            float gamePhase = GetGamePhase();
            float positionalScoreScale = 0.75f;

            if (gamePhase > 0.75f)  // Endgame
            {
                score += GetCentralizationBonus(PieceType.King, 400f * positionalScoreScale);
                score += GetCentralizationBonus(PieceType.Queen, 100f * positionalScoreScale);
                score -= GetCentralizationBonus(PieceType.Rook, 75f * positionalScoreScale);
                score += GetCentralizationBonus(PieceType.Bishop, 125f * positionalScoreScale);
                score += GetCentralizationBonus(PieceType.Knight, 150f * positionalScoreScale);
            }
            else if (gamePhase < 0.4f)  // Opening
            {
                score += GetCentralizationBonus(PieceType.King, -500f * positionalScoreScale);
                score += GetCentralizationBonus(PieceType.Queen, 100f * positionalScoreScale);
                score -= GetCentralizationBonus(PieceType.Rook, 80f * positionalScoreScale);
                score += GetCentralizationBonus(PieceType.Bishop, 120f * positionalScoreScale);
                score += GetCentralizationBonus(PieceType.Knight, 200f * positionalScoreScale);
                score += GetCentralizationBonus(PieceType.Pawn, 120f * positionalScoreScale);
            }
            else  // Middlegame
            {
                score += GetCentralizationBonus(PieceType.King, -250f * positionalScoreScale);
                score += GetCentralizationBonus(PieceType.Queen, 150f * positionalScoreScale);
                score -= GetCentralizationBonus(PieceType.Rook, 100f * positionalScoreScale);
                score += GetCentralizationBonus(PieceType.Bishop, 140f * positionalScoreScale);
                score += GetCentralizationBonus(PieceType.Knight, 165f * positionalScoreScale);
                score += GetCentralizationBonus(PieceType.Pawn, 100f * positionalScoreScale);
            }

            return board.Turn == PieceColor.White ? score : -score;
        }

        static float GetGamePhase()
        {
            // Standard piece values
            int Pawn = 1, Knight = 3, Bishop = 3, Rook = 5, Queen = 9;

            // Maximum material at start (excluding kings)
            int maxMaterial = (8 * Pawn) + (2 * Knight) + (2 * Bishop) + (2 * Rook) + (1 * Queen);
            maxMaterial *= 2; // both sides

            // Current material on board - direct board access instead of ASCII
            int currentMaterial = 0;

            for (int rank = 0; rank < 8; rank++)
            {
                for (int file = 0; file < 8; file++)
                {
                    var piece = board[file, rank];
                    if (piece == null) continue;

                    int pieceValue = 0;
                    if (piece.Type == PieceType.Pawn) pieceValue = Pawn;
                    else if (piece.Type == PieceType.Knight) pieceValue = Knight;
                    else if (piece.Type == PieceType.Bishop) pieceValue = Bishop;
                    else if (piece.Type == PieceType.Rook) pieceValue = Rook;
                    else if (piece.Type == PieceType.Queen) pieceValue = Queen;

                    currentMaterial += pieceValue;
                }
            }

            // Compute phase: 0 = opening, 1 = endgame
            float phase = 1f - ((float)currentMaterial / maxMaterial);

            // Clamp between 0 and 1
            if (phase < 0f) phase = 0f;
            if (phase > 1f) phase = 1f;

            return phase;
        }

        static float[] GetPieceRankDistanceBonus(PieceType type, int targetRank, float bonus, float scale, bool flip)
        {
            if (targetRank > 8) { targetRank = 8; } else if (targetRank < 1) { targetRank = 1; }

            targetRank--; // Convert to 0-based index

            List<float> rankDistanceValues = new List<float>();

            // Iterate through all squares on the board
            for (int rank = 0; rank < 8; rank++)
            {
                for (int file = 0; file < 8; file++)
                {
                    var piece = board[file, rank];
                    if (piece != null && piece.Type == type)
                    {
                        // Determine the actual target rank based on piece color and flip setting
                        int actualTargetRank = targetRank;
                        if (flip && piece.Color == PieceColor.Black)
                        {
                            // Flip the target rank for black: rank 7 becomes 0, rank 0 becomes 7, etc.
                            actualTargetRank = 7 - targetRank;
                        }

                        float rankDistance = CalculateRankDistance(rank, actualTargetRank);
                        float value = rankDistance * bonus * scale;

                        // Flip sign for black pieces
                        if (piece.Color == PieceColor.Black)
                            value = -value;

                        rankDistanceValues.Add(value);
                    }
                }
            }

            return rankDistanceValues.ToArray();
        }

        static float CalculateRankDistance(int currentRank, int targetRank)
        {
            // Maximum possible distance from target rank to board edge
            // For target rank 3: max distance is max(3-0, 7-3) = max(3, 4) = 4
            float maxDistanceToEdge = Math.Max(targetRank, 7 - targetRank);

            // Calculate absolute distance from target rank
            float distance = Math.Abs(currentRank - targetRank);

            // Normalize: 1 = on target rank (distance 0), 0 = furthest edge (max distance)
            float rankDistance = 1f - (distance / maxDistanceToEdge);

            // Clamp to [0, 1]
            if (rankDistance < 0f) rankDistance = 0f;
            if (rankDistance > 1f) rankDistance = 1f;

            return rankDistance;
        }

        static int GetRankBonus(PieceType type, int rank = 1, float bonus = 100, float weight = 1.0f, bool mirror = true)
        {
            var rankBonus = GetPieceRankDistanceBonus(type, rank, bonus, weight, mirror);
            return (int)(rankBonus.Sum());
        }

        static float[] GetPieceCentralization(PieceType type)
        {
            List<float> centralizationValues = new List<float>();

            // Direct board iteration instead of ASCII parsing
            for (int rank = 0; rank < 8; rank++)
            {
                for (int file = 0; file < 8; file++)
                {
                    var piece = board[file, rank];
                    if (piece != null && piece.Type == type)
                    {
                        float centralization = CalculateCentralization(file, rank);

                        // Flip sign for black pieces: white = [0, 1], black = [-1, 0]
                        if (piece.Color == PieceColor.Black)
                            centralization = -centralization;

                        centralizationValues.Add(centralization);
                    }
                }
            }

            return centralizationValues.ToArray();
        }

        static float CalculateCentralization(int file, int rank)
        {
            // Distance from center (0-3.5, where 3.5 is corner distance)
            // Center of board is between d4/d5/e4/e5 (files 3-4, ranks 3-4)
            float centerFile = 3.5f;
            float centerRank = 3.5f;
            float distFromCenterFile = Math.Abs(file - centerFile);
            float distFromCenterRank = Math.Abs(rank - centerRank);

            // Maximum distance from center (to corner) is approximately 3.5
            float maxDistance = 3.5f;

            // Chebyshev distance (max of file/rank distance)
            float distance = Math.Max(distFromCenterFile, distFromCenterRank);

            // Normalize: 0 = corner (distance 3.5), 1 = center (distance 0)
            float centralization = 1f - (distance / maxDistance);

            // Clamp to [0, 1]
            if (centralization < 0f) centralization = 0f;
            if (centralization > 1f) centralization = 1f;

            return centralization;
        }

        static int GetCentralizationBonus(PieceType type, float weight = 50f)
        {
            // ascii parameter now ignored - using direct board access
            var centralization = GetPieceCentralization(type);
            return (int)(centralization.Sum() * weight);
        }
    }
}
