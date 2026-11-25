# T3.10 (plan 5.8): an import cycle is a compile error naming the cycle. (The
# main file is named "cycle_a.ws" and imported as "cycle_a", so the cycle is
# reported from the first file imported twice: cycle_b -> cycle_a -> cycle_b.)
import "cycle_b"   # expect-compile-error: Circular import: cycle_b -> cycle_a -> cycle_b
