# T3.10 (plan 5.8): an import's own imports run before it.
import "order_base"
print "left"
# expect: base
# expect: left
