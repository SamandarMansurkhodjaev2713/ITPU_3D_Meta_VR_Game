using System;
using UnityEngine;

namespace ITPU.XR
{
    public enum BoardCommand
    {
        Marker,
        Eraser,
        Undo,
        ColorNext,
    }

    [DisallowMultipleComponent]
    public sealed class BoardCommandInteractable : MonoBehaviour
    {
        [SerializeField] private BoardDrawingController board;
        [SerializeField] private BoardCommand command;

        public BoardCommand Command => command;
        public int ActivationCount { get; private set; }
        public string LastSource { get; private set; } = "";

        public void Configure(BoardDrawingController drawingBoard, BoardCommand boardCommand)
        {
            board = drawingBoard;
            command = boardCommand;
        }

        public bool TryActivate(string source)
        {
            if (board == null || string.IsNullOrWhiteSpace(source) || !Enum.IsDefined(typeof(BoardCommand), command))
                return false;
            var accepted = command switch
            {
                BoardCommand.Marker => board.SelectTool(BoardTool.Marker),
                BoardCommand.Eraser => board.SelectTool(BoardTool.Eraser),
                BoardCommand.Undo => board.Undo(),
                BoardCommand.ColorNext => board.CycleMarkerColor(),
                _ => false,
            };
            if (!accepted) return false;
            checked { ActivationCount++; }
            LastSource = source;
            return true;
        }
    }
}
