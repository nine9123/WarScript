using NUnit.Framework;

namespace Tests
{
    [TestFixture]
    public class ConstEnumTests
    {
        [Test]
        public void BasicConst()
        {
            var (_, output) = TestHelper.Run("test", @"
                const MAX = 100
                print MAX
            ");
            Assert.AreEqual(new[] { "100" }, output);
        }

        [Test]
        public void ConstString()
        {
            var (_, output) = TestHelper.Run("test", @"
                const NAME = ""hello""
                print NAME
            ");
            Assert.AreEqual(new[] { "hello" }, output);
        }

        [Test]
        public void ConstInExpression()
        {
            var (_, output) = TestHelper.Run("test", @"
                const BASE = 10
                const MULT = 3
                print BASE * MULT
            ");
            Assert.AreEqual(new[] { "30" }, output);
        }

        [Test]
        public void ConstReassignmentThrows()
        {
            Assert.That(() =>
            {
                TestHelper.Run("test", @"
                    const X = 5
                    X = 10
                ");
            }, Throws.TypeOf<WarScript.Exception.SyntaxException>());
        }

        [Test]
        public void ConstCompoundAssignmentThrows()
        {
            Assert.That(() =>
            {
                TestHelper.Run("test", @"
                    const X = 5
                    X += 1
                ");
            }, Throws.TypeOf<WarScript.Exception.SyntaxException>());
        }

        [Test]
        public void ConstDuplicateThrows()
        {
            Assert.That(() =>
            {
                TestHelper.Run("test", @"
                    const X = 5
                    const X = 10
                ");
            }, Throws.TypeOf<WarScript.Exception.SyntaxException>());
        }

        // ── Enum tests ──

        [Test]
        public void BasicEnum()
        {
            var (_, output) = TestHelper.Run("test", @"
                enum Color
                    RED
                    GREEN
                    BLUE
                end
                print Color :: RED
                print Color :: GREEN
                print Color :: BLUE
            ");
            Assert.AreEqual(new[] { "0", "1", "2" }, output);
        }

        [Test]
        public void EnumExplicitValues()
        {
            var (_, output) = TestHelper.Run("test", @"
                enum State
                    IDLE = 0
                    RUNNING = 1
                    JUMPING = 5
                end
                print State :: IDLE
                print State :: JUMPING
            ");
            Assert.AreEqual(new[] { "0", "5" }, output);
        }

        [Test]
        public void EnumAutoIncrementAfterExplicit()
        {
            var (_, output) = TestHelper.Run("test", @"
                enum Prio
                    LOW = 10
                    MEDIUM
                    HIGH
                end
                print Prio :: LOW
                print Prio :: MEDIUM
                print Prio :: HIGH
            ");
            Assert.AreEqual(new[] { "10", "11", "12" }, output);
        }

        [Test]
        public void EnumNameMethod()
        {
            var (_, output) = TestHelper.Run("test", @"
                enum Dir
                    UP
                    DOWN
                    LEFT
                    RIGHT
                end
                print Dir :: name [Dir :: UP]
                print Dir :: name [Dir :: RIGHT]
                print Dir :: name [99]
            ");
            Assert.AreEqual(new[] { "UP", "RIGHT", "unknown" }, output);
        }

        [Test]
        public void EnumReassignmentThrows()
        {
            Assert.That(() =>
            {
                TestHelper.Run("test", @"
                    enum Team
                        RED
                        BLUE
                    end
                    Team = 99
                ");
            }, Throws.TypeOf<WarScript.Exception.SyntaxException>());
        }

        [Test]
        public void EnumInConditional()
        {
            var (_, output) = TestHelper.Run("test", @"
                enum Dir
                    UP
                    DOWN
                end
                fun describe [d]
                    if d == Dir :: UP
                        return ""up""
                    elif d == Dir :: DOWN
                        return ""down""
                    end
                    return ""other""
                end
                print describe [Dir :: UP]
                print describe [Dir :: DOWN]
            ");
            Assert.AreEqual(new[] { "up", "down" }, output);
        }

        [Test]
        public void EnumNameForDisplay()
        {
            var (_, output) = TestHelper.Run("test", @"
                enum DamageType
                    PHYSICAL
                    MAGICAL
                end
                t = DamageType :: MAGICAL
                print ""Damage: "" + DamageType :: name [t]
            ");
            Assert.AreEqual(new[] { "Damage: MAGICAL" }, output);
        }

        [Test]
        public void ConstAsDefaultParam()
        {
            var (_, output) = TestHelper.Run("test", @"
                const SPEED = 5
                fun move [x, speed = SPEED]
                    return x + speed
                end
                print move [10]
                print move [10, 20]
            ");
            Assert.AreEqual(new[] { "15", "30" }, output);
        }

        [Test]
        public void EnumValuesProperty()
        {
            var (_, output) = TestHelper.Run("test", @"
                enum Color
                    RED
                    GREEN
                    BLUE
                end
                print Color :: values{0}
                print Color :: values{1}
                print Color :: values{2}
            ");
            Assert.AreEqual(new[] { "0", "1", "2" }, output);
        }

        [Test]
        public void EnumNamesProperty()
        {
            var (_, output) = TestHelper.Run("test", @"
                enum Color
                    RED
                    GREEN
                    BLUE
                end
                print Color :: names{0}
                print Color :: names{1}
                print Color :: names{2}
            ");
            Assert.AreEqual(new[] { "RED", "GREEN", "BLUE" }, output);
        }

        [Test]
        public void EnumCountProperty()
        {
            var (_, output) = TestHelper.Run("test", @"
                enum Color
                    RED
                    GREEN
                    BLUE
                end
                print Color :: count
            ");
            Assert.AreEqual(new[] { "3" }, output);
        }

        [Test]
        public void LoopOverEnumValues()
        {
            var (_, output) = TestHelper.Run("test", @"
                enum Dir
                    UP
                    DOWN
                    LEFT
                    RIGHT
                end
                loop v in Dir :: values
                    print Dir :: name [v]
                end
            ");
            Assert.AreEqual(new[] { "UP", "DOWN", "LEFT", "RIGHT" }, output);
        }

        [Test]
        public void LoopOverEnumNames()
        {
            var (_, output) = TestHelper.Run("test", @"
                enum Dir
                    UP
                    DOWN
                end
                loop n in Dir :: names
                    print n
                end
            ");
            Assert.AreEqual(new[] { "UP", "DOWN" }, output);
        }

        [Test]
        public void FullTestScript()
        {
            var (_, output) = TestHelper.RunFile("test_const_enum.ws");
            Assert.That(output, Does.Contain("all const and enum tests passed"));
        }
    }
}
