using System;
using System.Collections.Generic;
using System.Linq;
using Chess;

namespace Sharp
{
    class Program
    {
        static ChessBoard board = new ChessBoard();
        static int searchDepth = 4;
        static int moveTime = 0; // milliseconds
        static DateTime searchStart;
        static long nodeCount = 0;
        static List<Move> principalVariation = new List<Move>();
        static List<Move> tempPv = new List<Move>();

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
                }
                else if (line == "quit")
                {
                    break;
                }
            }
        }

        // Convert LAN (g1f3) to Move object by matching against legal moves
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

        // ---------------- POSITION ----------------

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
                        board.Move(move); // Pass Move object directly
                    }
                    idx++;
                }
            }
        }

        // ---------------- GO ----------------

        static void ParseGo(string line)
        {
            var parts = line.Split(' ');
            moveTime = 0; // reset
            searchDepth = 4; // default

            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "depth" && i + 1 < parts.Length)
                    searchDepth = int.Parse(parts[i + 1]);
                else if (parts[i] == "movetime" && i + 1 < parts.Length)
                    moveTime = int.Parse(parts[i + 1]);
            }
        }

        // ---------------- SEARCH ----------------

        static string FindBestMove()
        {
            searchStart = DateTime.Now;
            nodeCount = 0;
            Move bestMove = board.Moves()[0];

            if (moveTime > 0)
            {
                // Iterative deepening with time limit
                for (int depth = 1; depth <= 256; depth++)
                {
                    principalVariation.Clear();
                    int alpha = int.MinValue + 1;
                    int beta = int.MaxValue;
                    int bestScore = int.MinValue;

                    foreach (var move in board.Moves())
                    {
                        if (TimeExpired())
                            break;

                        board.Move(move);
                        principalVariation.Clear();
                        int score = -Negamax(depth - 1, -beta, -alpha, principalVariation);
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
                // Fixed depth search
                principalVariation.Clear();
                int alpha = int.MinValue + 1;
                int beta = int.MaxValue;
                int bestScore = int.MinValue;

                foreach (var move in board.Moves())
                {
                    board.Move(move);
                    principalVariation.Clear();
                    int score = -Negamax(searchDepth - 1, -beta, -alpha, principalVariation);
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

            return MoveToUci(bestMove);
        }

        static bool TimeExpired()
        {
            return (DateTime.Now - searchStart).TotalMilliseconds >= moveTime;
        }

        static string MoveToUci(Move move)
        {
            string moveStr = move.ToString();
            // Extract the coordinate parts from "{wn - g1 - f3}" format
            var parts = moveStr.Split('-');
            if (parts.Length >= 3)
            {
                string from = parts[1].Trim();
                string to = parts[2].Trim().TrimEnd('}');
                return $"{from}{to}";
            }
            return moveStr; // fallback
        }

        static int Negamax(int depth, int alpha, int beta, List<Move> pv)
        {
            nodeCount++;

            if (moveTime > 0 && TimeExpired())
                return Evaluate();

            if (depth == 0 || board.IsEndGame)
                return Evaluate();

            int best = int.MinValue;
            List<Move> bestPv = new List<Move>();

            foreach (var move in board.Moves())
            {
                if (moveTime > 0 && TimeExpired())
                    break;

                board.Move(move);
                List<Move> childPv = new List<Move>();
                int score = -Negamax(depth - 1, -beta, -alpha, childPv);
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
            }

            pv.AddRange(bestPv);
            return best;
        }

        // ---------------- EVALUATION ----------------

        static int Evaluate()
        {
            int score = 0;
            string ascii = board.ToAscii();

            score += Count(ascii, 'P') * 100;
            score += Count(ascii, 'N') * 320;
            score += Count(ascii, 'B') * 330;
            score += Count(ascii, 'R') * 500;
            score += Count(ascii, 'Q') * 900;

            score -= Count(ascii, 'p') * 100;
            score -= Count(ascii, 'n') * 320;
            score -= Count(ascii, 'b') * 330;
            score -= Count(ascii, 'r') * 500;
            score -= Count(ascii, 'q') * 900;

            return board.Turn == PieceColor.White ? score : -score;
        }

        static int Count(string s, char c)
        {
            int n = 0;
            foreach (var x in s)
                if (x == c) n++;
            return n;
        }
    }
}
