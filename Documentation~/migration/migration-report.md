# WarScript migration report

Written by `Tools~/WarScript.Migrate` (plan T3.2, section 8.2) in `--report` mode: the rewrites below were not applied.

Inputs: `Tests/Golden~`, `Tests/resources`

149 files, 33 with rewrites; 139 entries: 105 rewrites, 34 need manual work.

| Rule | Meaning | Rewrites | Manual |
|---|---|---|---|
| `global-write` | A function or lambda writes a global | 17 | 0 |
| `field-access` | A method uses fields by bare name | 78 | 0 |
| `method-writes-global` | A method writes a name that is a global | 0 | 1 |
| `method-reads-global` | A method or class body reads a name that is a global | 0 | 1 |
| `import-order` | An import comes after other statements | 0 | 0 |
| `global-name` | A variable is named `global` | 0 | 0 |
| `text-subtraction` | Text subtraction with a literal | 10 | 1 |
| `block-escape` | A top-level block variable escapes its block | 0 | 3 |
| `dynamic-property` | A dynamic property is created | 0 | 1 |
| `text-index` | A text index is assigned | 0 | 25 |
| `parse-error` | The file does not parse with the v1 frontend | 0 | 2 |
| `import-unresolved` | An import names no file among the inputs | 0 | 0 |

## Tests/Golden~/algorithms/test_deep_algo_sieve.ws

- `Tests/Golden~/algorithms/test_deep_algo_sieve.ws:3:1` **global-write** (rewrite): Inserted `global primes` as the first statement of `sieve` (v1 wrote these globals). Decide whether each was intended or should become a local.
  ```
  fun sieve [limit]
  ```

## Tests/Golden~/deep/test_deep_arrays.ws

- `Tests/Golden~/deep/test_deep_arrays.ws:9:1` **global-write** (rewrite): Inserted `global m` as the first statement of `make_matrix` (v1 wrote these globals). Decide whether each was intended or should become a local.
  ```
  fun make_matrix [rows, cols, default_val]
  ```
- `Tests/Golden~/deep/test_deep_arrays.ws:46:1` **global-write** (rewrite): Inserted `global m` as the first statement of `make_identity` (v1 wrote these globals). Decide whether each was intended or should become a local.
  ```
  fun make_identity [n]
  ```

## Tests/Golden~/deep/test_deep_class_method_combos.ws

- `Tests/Golden~/deep/test_deep_class_method_combos.ws:169:23` **field-access** (rewrite): Replaced the bare field name `lo` with `@lo` in `Range#contains` (1 use).
  ```
          return val >= lo and val <= hi
  ```
- `Tests/Golden~/deep/test_deep_class_method_combos.ws:169:37` **field-access** (rewrite): Replaced the bare field name `hi` with `@hi` in `Range#contains` (1 use).
  ```
          return val >= lo and val <= hi
  ```
- `Tests/Golden~/deep/test_deep_class_method_combos.ws:172:16` **field-access** (rewrite): Replaced the bare field name `hi` with `@hi` in `Range#size` (1 use).
  ```
          return hi - lo
  ```
- `Tests/Golden~/deep/test_deep_class_method_combos.ws:172:21` **field-access** (rewrite): Replaced the bare field name `lo` with `@lo` in `Range#size` (1 use).
  ```
          return hi - lo
  ```
- `Tests/Golden~/deep/test_deep_class_method_combos.ws:175:16` **field-access** (rewrite): Replaced the bare field name `lo` with `@lo` in `Range#overlaps` (1 use).
  ```
          return lo < other :: hi and hi > other :: lo
  ```
- `Tests/Golden~/deep/test_deep_class_method_combos.ws:175:37` **field-access** (rewrite): Replaced the bare field name `hi` with `@hi` in `Range#overlaps` (1 use).
  ```
          return lo < other :: hi and hi > other :: lo
  ```

## Tests/Golden~/deep/test_deep_classes.ws

- `Tests/Golden~/deep/test_deep_classes.ws:12:29` **field-access** (rewrite): Replaced the bare field name `horsepower` with `@horsepower` in `Engine#describe` (1 use).
  ```
          return "Engine: " + horsepower + "hp"
  ```
- `Tests/Golden~/deep/test_deep_classes.ws:18:16` **field-access** (rewrite): Replaced the bare field name `make` with `@make` in `Car#full_description` (1 use).
  ```
          return make + " with " + this :: engine :: describe []
  ```
- `Tests/Golden~/deep/test_deep_classes.ws:115:29` **field-access** (rewrite): Replaced the bare field name `x` with `@x` in `Vector2#add` (1 use).
  ```
          return new Vector2 [x + other :: x, y + other :: y]
  ```
- `Tests/Golden~/deep/test_deep_classes.ws:115:45` **field-access** (rewrite): Replaced the bare field name `y` with `@y` in `Vector2#add` (1 use).
  ```
          return new Vector2 [x + other :: x, y + other :: y]
  ```
- `Tests/Golden~/deep/test_deep_classes.ws:118:29` **field-access** (rewrite): Replaced the bare field name `x` with `@x` in `Vector2#scale` (1 use).
  ```
          return new Vector2 [x * factor, y * factor]
  ```
- `Tests/Golden~/deep/test_deep_classes.ws:118:41` **field-access** (rewrite): Replaced the bare field name `y` with `@y` in `Vector2#scale` (1 use).
  ```
          return new Vector2 [x * factor, y * factor]
  ```
- `Tests/Golden~/deep/test_deep_classes.ws:121:16` **field-access** (rewrite): Replaced the bare field name `x` with `@x` in `Vector2#magnitude_squared` (2 uses).
  ```
          return x * x + y * y
  ```
- `Tests/Golden~/deep/test_deep_classes.ws:121:24` **field-access** (rewrite): Replaced the bare field name `y` with `@y` in `Vector2#magnitude_squared` (2 uses).
  ```
          return x * x + y * y
  ```
- `Tests/Golden~/deep/test_deep_classes.ws:124:16` **field-access** (rewrite): Replaced the bare field name `x` with `@x` in `Vector2#equals` (1 use).
  ```
          return x == other :: x and y == other :: y
  ```
- `Tests/Golden~/deep/test_deep_classes.ws:124:36` **field-access** (rewrite): Replaced the bare field name `y` with `@y` in `Vector2#equals` (1 use).
  ```
          return x == other :: x and y == other :: y
  ```
- `Tests/Golden~/deep/test_deep_classes.ws:183:31` **field-access** (rewrite): Replaced the bare field name `name` with `@name` in `Lifeform#identify` (1 use).
  ```
          return "Lifeform: " + name
  ```
- `Tests/Golden~/deep/test_deep_classes.ws:188:16` **field-access** (rewrite): Replaced the bare field name `name` with `@name` in `Animal#move` (1 use).
  ```
          return name + " moves on " + legs + " legs"
  ```
- `Tests/Golden~/deep/test_deep_classes.ws:188:38` **field-access** (rewrite): Replaced the bare field name `legs` with `@legs` in `Animal#move` (1 use).
  ```
          return name + " moves on " + legs + " legs"
  ```
- `Tests/Golden~/deep/test_deep_classes.ws:193:16` **field-access** (rewrite): Replaced the bare field name `name` with `@name` in `Pet#greet` (1 use).
  ```
          return name + " belongs to " + owner
  ```
- `Tests/Golden~/deep/test_deep_classes.ws:193:40` **field-access** (rewrite): Replaced the bare field name `owner` with `@owner` in `Pet#greet` (1 use).
  ```
          return name + " belongs to " + owner
  ```
- `Tests/Golden~/deep/test_deep_classes.ws:247:26` **field-access** (rewrite): Replaced the bare field name `data` with `@data` in `JsonSerializable#to_json` (1 use).
  ```
          return "data=" + data
  ```
- `Tests/Golden~/deep/test_deep_classes.ws:252:22` **field-access** (rewrite): Replaced the bare field name `label` with `@label` in `Printable#to_display` (1 use).
  ```
          return "[" + label + "]"
  ```
- `Tests/Golden~/deep/test_deep_classes.ws:257:46` **field-access** (rewrite): Replaced the bare field name `timestamp` with `@timestamp` in `LogEntry#full_log` (1 use).
  ```
          return this :: to_display [] + " " + timestamp + " " + this :: to_json []
  ```

## Tests/Golden~/deep/test_deep_control_flow.ws

- `Tests/Golden~/deep/test_deep_control_flow.ws:90:1` **global-write** (rewrite): Inserted `global low, mid, high, result` as the first statement of `categorize` (v1 wrote these globals). Decide whether each was intended or should become a local.
  ```
  fun categorize [values, n]
  ```
- `Tests/Golden~/deep/test_deep_control_flow.ws:163:9` **method-writes-global** (manual): `Processor#process` assigns `result`, which is a global: v1 made it a method local (globals were invisible in methods), and v2 does too, but the local now shadows the global. No rewrite; check that a local was meant.
  ```
          result = {}
  ```

## Tests/Golden~/deep/test_deep_exception_patterns.ws

- `Tests/Golden~/deep/test_deep_exception_patterns.ws:120:1` **global-write** (rewrite): Inserted `global resource_open` as the first statement of `use_resource` (v1 wrote these globals). Decide whether each was intended or should become a local.
  ```
  fun use_resource []
  ```

## Tests/Golden~/deep/test_deep_interp_combos.ws

- `Tests/Golden~/deep/test_deep_interp_combos.ws:35:18` **field-access** (rewrite): Replaced the bare field name `prefix` with `@prefix` in `Formatter#format` (1 use).
  ```
          return "{prefix}: {msg}"
  ```

## Tests/Golden~/deep/test_deep_multi_inherit.ws

- `Tests/Golden~/deep/test_deep_multi_inherit.ws:8:16` **field-access** (rewrite): Replaced the bare field name `id` with `@id` in `HasId#get_id` (1 use).
  ```
          return id
  ```
- `Tests/Golden~/deep/test_deep_multi_inherit.ws:13:16` **field-access** (rewrite): Replaced the bare field name `name` with `@name` in `HasName#get_name` (1 use).
  ```
          return name
  ```
- `Tests/Golden~/deep/test_deep_multi_inherit.ws:18:16` **field-access** (rewrite): Replaced the bare field name `email` with `@email` in `HasEmail#get_email` (1 use).
  ```
          return email
  ```
- `Tests/Golden~/deep/test_deep_multi_inherit.ws:59:16` **field-access** (rewrite): Replaced the bare field name `tag` with `@tag` in `Base#get_tag` (1 use).
  ```
          return tag
  ```
- `Tests/Golden~/deep/test_deep_multi_inherit.ws:64:16` **field-access** (rewrite): Replaced the bare field name `extra` with `@extra` in `Mixin#get_extra` (1 use).
  ```
          return extra
  ```
- `Tests/Golden~/deep/test_deep_multi_inherit.ws:71:72` **field-access** (rewrite): Replaced the bare field name `level` with `@level` in `Leaf#info` (1 use).
  ```
          return this :: get_tag [] + "/" + this :: get_extra [] + "/" + level + "/" + detail
  ```
- `Tests/Golden~/deep/test_deep_multi_inherit.ws:71:86` **field-access** (rewrite): Replaced the bare field name `detail` with `@detail` in `Leaf#info` (1 use).
  ```
          return this :: get_tag [] + "/" + this :: get_extra [] + "/" + level + "/" + detail
  ```
- `Tests/Golden~/deep/test_deep_multi_inherit.ws:93:16` **field-access** (rewrite): Replaced the bare field name `color` with `@color` in `Drawable#get_color` (1 use).
  ```
          return color
  ```

## Tests/Golden~/deep/test_deep_recursive_locals.ws

- `Tests/Golden~/deep/test_deep_recursive_locals.ws:198:1` **global-write** (rewrite): Inserted `global counter` as the first statement of `recursive_count` (v1 wrote these globals). Decide whether each was intended or should become a local.
  ```
  fun recursive_count [n]
  ```

## Tests/Golden~/deep/test_deep_scoping.ws

- `Tests/Golden~/deep/test_deep_scoping.ws:17:1` **global-write** (rewrite): Inserted `global counter` as the first statement of `bump_counter` (v1 wrote these globals). Decide whether each was intended or should become a local.
  ```
  fun bump_counter []
  ```
- `Tests/Golden~/deep/test_deep_scoping.ws:27:1` **global-write** (rewrite): Inserted `global shared` as the first statement of `add_to_shared` (v1 wrote these globals). Decide whether each was intended or should become a local.
  ```
  fun add_to_shared [n]
  ```
- `Tests/Golden~/deep/test_deep_scoping.ws:69:8` **block-escape** (manual): `inside_if` is first assigned inside the block at line 65 and read after it: v1 read null here, v2 reads the value.
  ```
  assert inside_if == null
  ```
- `Tests/Golden~/deep/test_deep_scoping.ws:134:16` **field-access** (rewrite): Replaced the bare field name `name` with `@name` in `Config#get_name` (1 use).
  ```
          return name
  ```
- `Tests/Golden~/deep/test_deep_scoping.ws:137:16` **field-access** (rewrite): Replaced the bare field name `value` with `@value` in `Config#get_value` (1 use).
  ```
          return value
  ```
- `Tests/Golden~/deep/test_deep_scoping.ws:140:9` **field-access** (rewrite): Replaced the bare field name `value` with `@value` in `Config#set_value` (1 use).
  ```
          value = v
  ```
- `Tests/Golden~/deep/test_deep_scoping.ws:176:8` **block-escape** (manual): `inside_begin` is first assigned inside the block at line 168 and read after it: v1 read null here, v2 reads the value.
  ```
  assert inside_begin == null
  ```
- `Tests/Golden~/deep/test_deep_scoping.ws:202:1` **global-write** (rewrite): Inserted `global dynamic` as the first statement of `change_dynamic` (v1 wrote these globals). Decide whether each was intended or should become a local.
  ```
  fun change_dynamic []
  ```

## Tests/Golden~/deep/test_deep_string_interpolation.ws

- `Tests/Golden~/deep/test_deep_string_interpolation.ws:39:28` **field-access** (rewrite): Replaced the bare field name `name` with `@name` in `Greeter#greet` (1 use).
  ```
          return "Hello, " + name + "!"
  ```
- `Tests/Golden~/deep/test_deep_string_interpolation.ws:92:20` **field-access** (rewrite): Replaced the bare field name `name` with `@name` in `TableRow#format` (1 use).
  ```
          return "| {name} | {value} {unit} |"
  ```
- `Tests/Golden~/deep/test_deep_string_interpolation.ws:92:29` **field-access** (rewrite): Replaced the bare field name `value` with `@value` in `TableRow#format` (1 use).
  ```
          return "| {name} | {value} {unit} |"
  ```
- `Tests/Golden~/deep/test_deep_string_interpolation.ws:92:37` **field-access** (rewrite): Replaced the bare field name `unit` with `@unit` in `TableRow#format` (1 use).
  ```
          return "| {name} | {value} {unit} |"
  ```

## Tests/Golden~/gapfill/test_gapfill_cross_feature.ws

- `Tests/Golden~/gapfill/test_gapfill_cross_feature.ws:182:1` **global-write** (rewrite): Inserted `global cleanup_ran` as the first statement of `with_ensure` (v1 wrote these globals). Decide whether each was intended or should become a local.
  ```
  fun with_ensure
  ```

## Tests/Golden~/gapfill/test_gapfill_string_mutation.ws

- `Tests/Golden~/gapfill/test_gapfill_string_mutation.ws:9:2` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
  s{0} = "X"
  ```
- `Tests/Golden~/gapfill/test_gapfill_string_mutation.ws:12:2` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
  s{4} = "Y"
  ```
- `Tests/Golden~/gapfill/test_gapfill_string_mutation.ws:15:2` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
  s{2} = "Z"
  ```
- `Tests/Golden~/gapfill/test_gapfill_string_mutation.ws:20:2` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
  t{1} = "a"
  ```
- `Tests/Golden~/gapfill/test_gapfill_string_mutation.ws:22:2` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
  t{0} = "H"
  ```
- `Tests/Golden~/gapfill/test_gapfill_string_mutation.ws:24:2` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
  t{4} = "!"
  ```
- `Tests/Golden~/gapfill/test_gapfill_string_mutation.ws:29:2` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
  r{0} = "x"
  ```
- `Tests/Golden~/gapfill/test_gapfill_string_mutation.ws:30:2` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
  r{1} = "y"
  ```
- `Tests/Golden~/gapfill/test_gapfill_string_mutation.ws:31:2` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
  r{2} = "z"
  ```
- `Tests/Golden~/gapfill/test_gapfill_string_mutation.ws:36:2` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
  d{0} = "1"
  ```
- `Tests/Golden~/gapfill/test_gapfill_string_mutation.ws:37:2` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
  d{1} = "2"
  ```
- `Tests/Golden~/gapfill/test_gapfill_string_mutation.ws:38:2` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
  d{2} = "3"
  ```
- `Tests/Golden~/gapfill/test_gapfill_string_mutation.ws:43:2` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
  m{1} = "b"
  ```
- `Tests/Golden~/gapfill/test_gapfill_string_mutation.ws:45:2` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
  m{1} = "c"
  ```
- `Tests/Golden~/gapfill/test_gapfill_string_mutation.ws:47:2` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
  m{1} = "d"
  ```
- `Tests/Golden~/gapfill/test_gapfill_string_mutation.ws:75:13` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
          word{i} = "a"
  ```
- `Tests/Golden~/gapfill/test_gapfill_string_mutation.ws:77:13` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
          word{i} = "b"
  ```
- `Tests/Golden~/gapfill/test_gapfill_string_mutation.ws:79:13` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
          word{i} = "c"
  ```
- `Tests/Golden~/gapfill/test_gapfill_string_mutation.ws:81:13` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
          word{i} = "d"
  ```

## Tests/Golden~/language/constructor_frames.ws

- `Tests/Golden~/language/constructor_frames.ws:63:16` **field-access** (rewrite): Replaced the bare field name `v` with `@v` in `Leaf2#twice` (1 use).
  ```
          return v * 2
  ```

## Tests/Golden~/language/test_arithmetic_operators.ws

- `Tests/Golden~/language/test_arithmetic_operators.ws:73:22` **text-subtraction** (rewrite): Rewrote `a - b` as `Text_remove [a, b]` (text subtraction is removed in v2, PROP-7).
  ```
  assert "hello world" - "world" == "hello "
  ```
- `Tests/Golden~/language/test_arithmetic_operators.ws:74:15` **text-subtraction** (rewrite): Rewrote `a - b` as `Text_remove [a, b]` (text subtraction is removed in v2, PROP-7).
  ```
  assert "test" - "xyz" == "test"
  ```

## Tests/Golden~/language/test_classes_basic.ws

- `Tests/Golden~/language/test_classes_basic.ws:53:9` **field-access** (rewrite): Replaced the bare field name `n` with `@n` in `Counter#increment` (2 uses).
  ```
          n = n + 1
  ```
- `Tests/Golden~/language/test_classes_basic.ws:56:16` **field-access** (rewrite): Replaced the bare field name `n` with `@n` in `Counter#get` (1 use).
  ```
          return n
  ```
- `Tests/Golden~/language/test_classes_basic.ws:145:16` **field-access** (rewrite): Replaced the bare field name `base_atk` with `@base_atk` in `Stats#get_atk` (1 use).
  ```
          return base_atk
  ```

## Tests/Golden~/language/test_classes_cast_instanceof.ws

- `Tests/Golden~/language/test_classes_cast_instanceof.ws:138:16` **field-access** (rewrite): Replaced the bare field name `id` with `@id` in `Identifiable#get_id` (1 use).
  ```
          return id
  ```
- `Tests/Golden~/language/test_classes_cast_instanceof.ws:143:16` **field-access** (rewrite): Replaced the bare field name `description` with `@description` in `Describable#describe` (1 use).
  ```
          return description
  ```

## Tests/Golden~/language/test_classes_inheritance.ws

- `Tests/Golden~/language/test_classes_inheritance.ws:20:28` **field-access** (rewrite): Replaced the bare field name `speed` with `@speed` in `Vehicle#describe` (1 use).
  ```
          return "speed: " + speed
  ```
- `Tests/Golden~/language/test_classes_inheritance.ws:47:31` **field-access** (rewrite): Replaced the bare field name `altitude` with `@altitude` in `Flyable#fly` (1 use).
  ```
          return "flying at " + altitude
  ```
- `Tests/Golden~/language/test_classes_inheritance.ws:52:33` **field-access** (rewrite): Replaced the bare field name `depth` with `@depth` in `Swimmable#swim` (1 use).
  ```
          return "swimming at " + depth
  ```
- `Tests/Golden~/language/test_classes_inheritance.ws:128:25` **field-access** (rewrite): Replaced the bare field name `a` with `@a` in `GrandParent#gp_method` (1 use).
  ```
          return "gp: " + a
  ```
- `Tests/Golden~/language/test_classes_inheritance.ws:133:24` **field-access** (rewrite): Replaced the bare field name `b` with `@b` in `Parent#p_method` (1 use).
  ```
          return "p: " + b
  ```
- `Tests/Golden~/language/test_classes_inheritance.ws:138:24` **field-access** (rewrite): Replaced the bare field name `c` with `@c` in `Child#c_method` (1 use).
  ```
          return "c: " + c
  ```

## Tests/Golden~/language/test_edge_cases.ws

- `Tests/Golden~/language/test_edge_cases.ws:147:16` **field-access** (rewrite): Replaced the bare field name `n` with `@n` in `ValueBox#doubled` (1 use).
  ```
          return n * 2
  ```

## Tests/Golden~/language/test_functions.ws

- `Tests/Golden~/language/test_functions.ws:174:1` **global-write** (rewrite): Inserted `global global_counter` as the first statement of `bump` (v1 wrote these globals). Decide whether each was intended or should become a local.
  ```
  fun bump []
  ```

## Tests/Golden~/language/test_integration.ws

- `Tests/Golden~/language/test_integration.ws:175:18` **field-access** (rewrite): Replaced the bare field name `label` with `@label` in `Printable#to_str` (1 use).
  ```
          return "{label}"
  ```
- `Tests/Golden~/language/test_integration.ws:181:26` **field-access** (rewrite): Replaced the bare field name `data` with `@data` in `Serializable#serialize` (1 use).
  ```
          return "data:" + data
  ```

## Tests/Golden~/language/test_logical_operators.ws

- `Tests/Golden~/language/test_logical_operators.ws:73:1` **global-write** (rewrite): Inserted `global side_effect_ran` as the first statement of `set_side_effect` (v1 wrote these globals). Decide whether each was intended or should become a local.
  ```
  fun set_side_effect []
  ```
- `Tests/Golden~/language/test_logical_operators.ws:82:1` **global-write** (rewrite): Inserted `global side_effect_ran2` as the first statement of `set_side_effect2` (v1 wrote these globals). Decide whether each was intended or should become a local.
  ```
  fun set_side_effect2 []
  ```
- `Tests/Golden~/language/test_logical_operators.ws:91:1` **global-write** (rewrite): Inserted `global side_effect_ran3` as the first statement of `set_side_effect3` (v1 wrote these globals). Decide whether each was intended or should become a local.
  ```
  fun set_side_effect3 []
  ```
- `Tests/Golden~/language/test_logical_operators.ws:100:1` **global-write** (rewrite): Inserted `global side_effect_ran4` as the first statement of `set_side_effect4` (v1 wrote these globals). Decide whether each was intended or should become a local.
  ```
  fun set_side_effect4 []
  ```

## Tests/Golden~/language/test_math_library.ws

- `Tests/Golden~/language/test_math_library.ws:121:30` **field-access** (rewrite): Replaced the bare field name `radius` with `@radius` in `Circle#area` (1 use).
  ```
          return 3.14159 * pow[radius, 2]
  ```
- `Tests/Golden~/language/test_math_library.ws:124:30` **field-access** (rewrite): Replaced the bare field name `radius` with `@radius` in `Circle#circumference` (1 use).
  ```
          return 2 * 3.14159 * radius
  ```

## Tests/Golden~/language/test_nested_classes.ws

- `Tests/Golden~/language/test_nested_classes.ws:22:20` **field-access** (rewrite): Replaced the bare field name `name` with `@name` in `Product#describe` (1 use).
  ```
              return name + ": $" + price
  ```
- `Tests/Golden~/language/test_nested_classes.ws:22:35` **field-access** (rewrite): Replaced the bare field name `price` with `@price` in `Product#describe` (1 use).
  ```
              return name + ": $" + price
  ```
- `Tests/Golden~/language/test_nested_classes.ws:53:9` **field-access** (rewrite): Replaced the bare field name `entity_count` with `@entity_count` in `GameWorld#spawn` (1 use).
  ```
          entity_count += 1
  ```

## Tests/Golden~/language/test_string_interpolation.ws

- `Tests/Golden~/language/test_string_interpolation.ws:86:19` **field-access** (rewrite): Replaced the bare field name `x` with `@x` in `Point#to_string` (1 use).
  ```
          return "({x}, {y})"
  ```
- `Tests/Golden~/language/test_string_interpolation.ws:86:24` **field-access** (rewrite): Replaced the bare field name `y` with `@y` in `Point#to_string` (1 use).
  ```
          return "({x}, {y})"
  ```

## Tests/Golden~/language/test_string_operations.ws

- `Tests/Golden~/language/test_string_operations.ws:34:22` **text-subtraction** (rewrite): Rewrote `a - b` as `Text_remove [a, b]` (text subtraction is removed in v2, PROP-7).
  ```
  assert "hello world" - "world" == "hello "
  ```
- `Tests/Golden~/language/test_string_operations.ws:35:22` **text-subtraction** (rewrite): Rewrote `a - b` as `Text_remove [a, b]` (text subtraction is removed in v2, PROP-7).
  ```
  assert "hello world" - "hello " == "world"
  ```
- `Tests/Golden~/language/test_string_operations.ws:36:17` **text-subtraction** (rewrite): Rewrote `a - b` as `Text_remove [a, b]` (text subtraction is removed in v2, PROP-7).
  ```
  assert "abcabc" - "abc" == ""
  ```
- `Tests/Golden~/language/test_string_operations.ws:37:15` **text-subtraction** (rewrite): Rewrote `a - b` as `Text_remove [a, b]` (text subtraction is removed in v2, PROP-7).
  ```
  assert "test" - "xyz" == "test"
  ```
- `Tests/Golden~/language/test_string_operations.ws:38:14` **text-subtraction** (rewrite): Rewrote `a - b` as `Text_remove [a, b]` (text subtraction is removed in v2, PROP-7).
  ```
  assert "aaa" - "a" == ""
  ```
- `Tests/Golden~/language/test_string_operations.ws:39:17` **text-subtraction** (rewrite): Rewrote `a - b` as `Text_remove [a, b]` (text subtraction is removed in v2, PROP-7).
  ```
  assert "banana" - "na" == "ba"
  ```

## Tests/Golden~/language/test_variables_and_scoping.ws

- `Tests/Golden~/language/test_variables_and_scoping.ws:90:1` **global-write** (rewrite): Inserted `global counter` as the first statement of `increment_counter` (v1 wrote these globals). Decide whether each was intended or should become a local.
  ```
  fun increment_counter []
  ```

## Tests/Golden~/language/unbalanced_paren.ws

- `Tests/Golden~/language/unbalanced_paren.ws:3:5` **parse-error** (manual): Not migrated: Unbalanced '(' at line 3: missing ')'
  ```
  x = (1 + 2   # expect-compile-error: Unbalanced '('
  ```

## Tests/Golden~/regression/test_regression_textvalue_setvalue.ws

- `Tests/Golden~/regression/test_regression_textvalue_setvalue.ws:11:5` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
  text{0} = "X"
  ```
- `Tests/Golden~/regression/test_regression_textvalue_setvalue.ws:15:6` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
  text2{2} = "Z"
  ```
- `Tests/Golden~/regression/test_regression_textvalue_setvalue.ws:19:6` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
  text3{4} = "Y"
  ```
- `Tests/Golden~/regression/test_regression_textvalue_setvalue.ws:24:2` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
  s{1} = "a"
  ```
- `Tests/Golden~/regression/test_regression_textvalue_setvalue.ws:29:3` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
  s2{0} = "W"
  ```

## Tests/Golden~/repro/A04_text_minus_empty.ws

- `Tests/Golden~/repro/A04_text_minus_empty.ws:3:11` **text-subtraction** (rewrite): Rewrote `a - b` as `Text_remove [a, b]` (text subtraction is removed in v2, PROP-7).
  ```
  x = "abc" - ""
  ```

## Tests/Golden~/repro/A10_function_writes_global.ws

- `Tests/Golden~/repro/A10_function_writes_global.ws:4:1` **global-write** (rewrite): Inserted `global count` as the first statement of `helper` (v1 wrote these globals). Decide whether each was intended or should become a local.
  ```
  fun helper []
  ```

## Tests/Golden~/repro/A12h_number_minus_text.ws

- `Tests/Golden~/repro/A12h_number_minus_text.ws:3:7` **text-subtraction** (manual): Subtracts text and a number: v2 raises TypeError (DEC-8). Decide what was meant; not rewritten as `Text_remove`.
  ```
  x = 5 - "5"   # expect-error: TypeError
  ```

## Tests/Golden~/repro/A13e_property_set_on_null.ws

- `Tests/Golden~/repro/A13e_property_set_on_null.ws:4:3` **dynamic-property** (manual): `obj :: hp = ...` creates the property `hp`, which is not a field of any class in the module set: declare the field (v2 has no dynamic properties).
  ```
  x :: hp = 5       # expect-error: TypeError
  ```

## Tests/Golden~/repro/A13f_text_index_set.ws

- `Tests/Golden~/repro/A13f_text_index_set.ws:4:2` **text-index** (manual): Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead.
  ```
  s{0} = "x"        # expect-error: TypeError
  ```

## Tests/Golden~/repro/A16_globals_in_methods.ws

- `Tests/Golden~/repro/A16_globals_in_methods.ws:6:16` **method-reads-global** (manual): `C#m` reads `g`, which is neither a field nor a local but a global: v1 read null here, v2 reads the global's value.
  ```
          return g
  ```

## Tests/Golden~/repro/A17_top_level_block_scope.ws

- `Tests/Golden~/repro/A17_top_level_block_scope.ws:6:7` **block-escape** (manual): `made` is first assigned inside the block at line 3 and read after it: v1 read null here, v2 reads the value.
  ```
  print made    # expect: 1
  ```

## Tests/Golden~/repro/A23a_parser_exception_operator.ws

- `Tests/Golden~/repro/A23a_parser_exception_operator.ws:4:5` **parse-error** (manual): Not migrated: Unbalanced '(' at line 4: missing ')'
  ```
  x = (1]   # expect-compile-error:
  ```

## Tests/resources/calculator.ws

- `Tests/resources/calculator.ws:3:16` **field-access** (rewrite): Replaced the bare field name `x` with `@x` in `Add#sum` (1 use).
  ```
          return x + y
  ```
- `Tests/resources/calculator.ws:3:20` **field-access** (rewrite): Replaced the bare field name `y` with `@y` in `Add#sum` (1 use).
  ```
          return x + y
  ```
- `Tests/resources/calculator.ws:9:16` **field-access** (rewrite): Replaced the bare field name `a` with `@a` in `Mul#mul` (1 use).
  ```
          return a * b
  ```
- `Tests/resources/calculator.ws:9:20` **field-access** (rewrite): Replaced the bare field name `b` with `@b` in `Mul#mul` (1 use).
  ```
          return a * b
  ```
- `Tests/resources/calculator.ws:15:16` **field-access** (rewrite): Replaced the bare field name `a` with `@a` in `Sub#sub` (1 use).
  ```
          return a - b
  ```
- `Tests/resources/calculator.ws:15:20` **field-access** (rewrite): Replaced the bare field name `b` with `@b` in `Sub#sub` (1 use).
  ```
          return a - b
  ```
- `Tests/resources/calculator.ws:21:16` **field-access** (rewrite): Replaced the bare field name `m` with `@m` in `Div#div` (1 use).
  ```
          return m / n
  ```
- `Tests/resources/calculator.ws:21:20` **field-access** (rewrite): Replaced the bare field name `n` with `@n` in `Div#div` (1 use).
  ```
          return m / n
  ```
- `Tests/resources/calculator.ws:27:20` **field-access** (rewrite): Replaced the bare field name `m` with `@m` in `Exp#exp` (1 use).
  ```
          return pow[m, n]
  ```
- `Tests/resources/calculator.ws:27:23` **field-access** (rewrite): Replaced the bare field name `n` with `@n` in `Exp#exp` (1 use).
  ```
          return pow[m, n]
  ```
- `Tests/resources/calculator.ws:33:22` **field-access** (rewrite): Replaced the bare field name `n` with `@n` in `Fib#fib` (1 use).
  ```
          return fib [ n ]
  ```

## Tests/resources/class_creation.ws

- `Tests/resources/class_creation.ws:28:9` **field-access** (rewrite): Replaced the bare field name `n` with `@n` in `Counter#increment` (2 uses).
  ```
          n = n + 1
  ```

## Tests/resources/stack.ws

- `Tests/resources/stack.ws:28:9` **field-access** (rewrite): Replaced the bare field name `n` with `@n` in `Stack#push` (2 uses).
  ```
          n = n + 1
  ```
- `Tests/resources/stack.ws:32:9` **field-access** (rewrite): Replaced the bare field name `n` with `@n` in `Stack#pop` (4 uses).
  ```
          n = n - 1
  ```
- `Tests/resources/stack.ws:33:16` **field-access** (rewrite): Replaced the bare field name `arr` with `@arr` in `Stack#pop` (2 uses).
  ```
          item = arr { n }
  ```

## Tests/resources/test_default_params.ws

- `Tests/resources/test_default_params.ws:55:9` **field-access** (rewrite): Replaced the bare field name `n` with `@n` in `Counter#add` (2 uses).
  ```
          n = n + amount
  ```

## Tests/resources/test_string_literals.ws

- `Tests/resources/test_string_literals.ws:14:15` **text-subtraction** (rewrite): Rewrote `a - b` as `Text_remove [a, b]` (text subtraction is removed in v2, PROP-7).
  ```
  assert "a\\b" - "\\" == "ab"
  ```
