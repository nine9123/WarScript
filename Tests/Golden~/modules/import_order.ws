# T3.10 (LANGUAGE.md section 8, plan 5.8): each file's top level runs exactly
# once per instance, imports first, in a depth-first post-order over the
# imports in textual order; order_base.ws, imported twice, runs once. All
# files form one namespace: a function of an import of an import is called
# by name.
import "order_right"
import "order_left"
print "main"
print base_name []
# expect: base
# expect: right
# expect: left
# expect: main
# expect: base
