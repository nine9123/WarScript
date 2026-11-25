# T3.10 (plan 5.8): the other half of cycle_a.ws.
import "cycle_a"   # expect-compile-error: Circular import: cycle_a -> cycle_b -> cycle_a
