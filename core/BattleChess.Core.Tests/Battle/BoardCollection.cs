using Xunit;

namespace BattleChess.Tests.Battle
{
    /// <summary>
    /// Every test class that touches the board shares this, so that none of them
    /// runs beside another.
    /// </summary>
    /// <remarks>
    /// <b>[M156].</b> The board's cell size, lattice shape and facing count are
    /// settings on a static, and two of these classes change them. xunit runs
    /// test classes in parallel, so without this a theory measuring a 12,5 m
    /// board re-scales the board another class is in the middle of asserting
    /// about. One collection means one at a time.
    /// </remarks>
    [CollectionDefinition("the board")]
    public sealed class BoardCollection { }

}
