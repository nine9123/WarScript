# T3.10 (plan 5.8): an import's own imports run before it.
import "order_base"
print "right"
# expect: base
# expect: right
