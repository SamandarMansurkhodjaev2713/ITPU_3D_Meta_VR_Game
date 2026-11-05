using System;
using ITPU.Domain;
using NUnit.Framework;

namespace ITPU.XR.Tests.EditMode
{
    public sealed class BoardDomainUnityTests
    {
        [Test]
        public void MarkerEraserAndUndo_ProduceBoundedObservableStates()
        {
            var board = new DrawingSurface();
            Assert.That(board.Begin(), Is.True);
            Assert.That(board.Line(0.15, 0.5, 0.85, 0.5, 3, 1), Is.True);
            Assert.That(board.End(), Is.True);
            var markerPixels = board.NonEmptyPixels();
            Assert.That(markerPixels, Is.GreaterThan(100));

            Assert.That(board.Begin(), Is.True);
            Assert.That(board.Line(0.48, 0.5, 0.52, 0.5, 9, 0), Is.True);
            Assert.That(board.End(), Is.True);
            var erasedPixels = board.NonEmptyPixels();
            Assert.That(erasedPixels, Is.GreaterThan(0).And.LessThan(markerPixels));

            Assert.That(board.Undo(), Is.True);
            Assert.That(board.NonEmptyPixels(), Is.EqualTo(markerPixels));
            Assert.That(board.Undo(), Is.True);
            Assert.That(board.NonEmptyPixels(), Is.Zero);
            Assert.That(board.Undo(), Is.False);
        }

        [Test]
        public void CancelRestoresBeforeStrokeAndDoesNotAddUndo()
        {
            var board = new DrawingSurface();
            Assert.That(board.Begin(), Is.True);
            Assert.That(board.Line(0.1, 0.2, 0.8, 0.8, 3, 2), Is.True);
            Assert.That(board.Cancel(), Is.True);
            Assert.That(board.NonEmptyPixels(), Is.Zero);
            Assert.That(board.Undo(), Is.False);

            Assert.That(board.Begin(), Is.True);
            Assert.That(board.End(), Is.False);
            Assert.That(board.Undo(), Is.False);
        }

        [Test]
        public void UndoHistory_IsStrictlyCappedAtTwelveSnapshots()
        {
            var board = new DrawingSurface(128, 64, 12);
            for (var index = 0; index < 13; index++)
            {
                var x = 0.04 + index * 0.07;
                Assert.That(board.Begin(), Is.True);
                Assert.That(board.Line(x, 0.5, x, 0.5, 1, 1), Is.True);
                Assert.That(board.End(), Is.True);
            }
            for (var index = 0; index < 12; index++) Assert.That(board.Undo(), Is.True);
            Assert.That(board.Undo(), Is.False);
            Assert.That(board.NonEmptyPixels(), Is.GreaterThan(0),
                "the oldest committed stroke must remain after the bounded history is exhausted");
        }

        [Test]
        public void ResetClearsPixelsHistoryAndActiveStroke()
        {
            var board = new DrawingSurface();
            Assert.That(board.Begin(), Is.True);
            board.Line(0.2, 0.2, 0.8, 0.8, 3, 1);
            board.Reset();
            Assert.That(board.NonEmptyPixels(), Is.Zero);
            Assert.That(board.Undo(), Is.False);
            Assert.That(board.End(), Is.False);
            Assert.That(board.Begin(), Is.True);
        }

        [Test]
        public void InvalidPaletteRadiusUvAndBuffer_AreRejected()
        {
            var board = new DrawingSurface();
            Assert.That(board.Begin(), Is.True);
            Assert.Throws<ArgumentException>(() => board.Line(0, 0, 1, 1, 2, 6));
            Assert.Throws<ArgumentException>(() => board.Line(0, 0, 1, 1, 0.4, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => board.Line(double.NaN, 0, 1, 1, 2, 1));
            Assert.Throws<ArgumentException>(() => board.CopyPixels(new byte[4]));
            board.Cancel();
        }

        [Test]
        public void SnapshotIsImmutableAndCopyPixelsReusesCallerBuffer()
        {
            var board = new DrawingSurface(32, 16, 2);
            Assert.That(board.Begin(), Is.True);
            board.Line(0.5, 0.5, 0.5, 0.5, 2, 5);
            board.End();
            var snapshot = board.Snapshot();
            var copy = new byte[32 * 16];
            board.CopyPixels(copy);
            Assert.That(copy, Is.EqualTo(snapshot));
            Array.Clear(snapshot, 0, snapshot.Length);
            Assert.That(board.NonEmptyPixels(), Is.GreaterThan(0));
        }
    }
}
