# WarScript migration report

Written by `Tools~/WarScript.Migrate` (plan T3.2, section 8.2) in `--report` mode: the rewrites below were not applied.

Inputs: `warman`

166 files, 0 with rewrites; 120 entries: 0 rewrites, 120 need manual work.

| Rule | Meaning | Rewrites | Manual |
|---|---|---|---|
| `global-write` | A function or lambda writes a global | 0 | 0 |
| `field-access` | A method uses fields by bare name | 0 | 0 |
| `method-writes-global` | A method writes a name that is a global | 0 | 0 |
| `method-reads-global` | A method or class body reads a name that is a global | 0 | 0 |
| `import-order` | An import comes after other statements | 0 | 120 |
| `global-name` | A variable is named `global` | 0 | 0 |
| `text-subtraction` | Text subtraction with a literal | 0 | 0 |
| `block-escape` | A top-level block variable escapes its block | 0 | 0 |
| `dynamic-property` | A dynamic property is created | 0 | 0 |
| `text-index` | A text index is assigned | 0 | 0 |
| `parse-error` | The file does not parse with the v1 frontend | 0 | 0 |
| `import-unresolved` | An import names no file among the inputs | 0 | 0 |

## warman/translation/de.ws

- `warman/translation/de.ws:7:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/de/menus.ws"
  ```
- `warman/translation/de.ws:8:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/de/options.ws"
  ```
- `warman/translation/de.ws:9:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/de/alerts.ws"
  ```
- `warman/translation/de.ws:10:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/de/tooltips.ws"
  ```
- `warman/translation/de.ws:11:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/de/skills.ws"
  ```
- `warman/translation/de.ws:12:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/de/mastery.ws"
  ```
- `warman/translation/de.ws:13:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/de/effects.ws"
  ```
- `warman/translation/de.ws:14:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/de/items.ws"
  ```
- `warman/translation/de.ws:15:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/de/characters.ws"
  ```
- `warman/translation/de.ws:16:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/de/world.ws"
  ```
- `warman/translation/de.ws:17:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/de/story.ws"
  ```
- `warman/translation/de.ws:18:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/de/multiplayer.ws"
  ```
- `warman/translation/de.ws:19:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/de/workshop.ws"
  ```
- `warman/translation/de.ws:20:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/de/codex.ws"
  ```
- `warman/translation/de.ws:21:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/de/general.ws"
  ```

## warman/translation/en.ws

- `warman/translation/en.ws:7:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/en/menus.ws"
  ```
- `warman/translation/en.ws:8:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/en/options.ws"
  ```
- `warman/translation/en.ws:9:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/en/alerts.ws"
  ```
- `warman/translation/en.ws:10:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/en/tooltips.ws"
  ```
- `warman/translation/en.ws:11:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/en/skills.ws"
  ```
- `warman/translation/en.ws:12:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/en/mastery.ws"
  ```
- `warman/translation/en.ws:13:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/en/effects.ws"
  ```
- `warman/translation/en.ws:14:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/en/items.ws"
  ```
- `warman/translation/en.ws:15:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/en/characters.ws"
  ```
- `warman/translation/en.ws:16:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/en/world.ws"
  ```
- `warman/translation/en.ws:17:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/en/story.ws"
  ```
- `warman/translation/en.ws:18:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/en/multiplayer.ws"
  ```
- `warman/translation/en.ws:19:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/en/workshop.ws"
  ```
- `warman/translation/en.ws:20:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/en/codex.ws"
  ```
- `warman/translation/en.ws:21:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/en/general.ws"
  ```

## warman/translation/es.ws

- `warman/translation/es.ws:7:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/es/menus.ws"
  ```
- `warman/translation/es.ws:8:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/es/options.ws"
  ```
- `warman/translation/es.ws:9:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/es/alerts.ws"
  ```
- `warman/translation/es.ws:10:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/es/tooltips.ws"
  ```
- `warman/translation/es.ws:11:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/es/skills.ws"
  ```
- `warman/translation/es.ws:12:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/es/mastery.ws"
  ```
- `warman/translation/es.ws:13:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/es/effects.ws"
  ```
- `warman/translation/es.ws:14:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/es/items.ws"
  ```
- `warman/translation/es.ws:15:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/es/characters.ws"
  ```
- `warman/translation/es.ws:16:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/es/world.ws"
  ```
- `warman/translation/es.ws:17:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/es/story.ws"
  ```
- `warman/translation/es.ws:18:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/es/multiplayer.ws"
  ```
- `warman/translation/es.ws:19:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/es/workshop.ws"
  ```
- `warman/translation/es.ws:20:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/es/codex.ws"
  ```
- `warman/translation/es.ws:21:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/es/general.ws"
  ```

## warman/translation/ja.ws

- `warman/translation/ja.ws:7:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ja/menus.ws"
  ```
- `warman/translation/ja.ws:8:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ja/options.ws"
  ```
- `warman/translation/ja.ws:9:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ja/alerts.ws"
  ```
- `warman/translation/ja.ws:10:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ja/tooltips.ws"
  ```
- `warman/translation/ja.ws:11:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ja/skills.ws"
  ```
- `warman/translation/ja.ws:12:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ja/mastery.ws"
  ```
- `warman/translation/ja.ws:13:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ja/effects.ws"
  ```
- `warman/translation/ja.ws:14:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ja/items.ws"
  ```
- `warman/translation/ja.ws:15:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ja/characters.ws"
  ```
- `warman/translation/ja.ws:16:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ja/world.ws"
  ```
- `warman/translation/ja.ws:17:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ja/story.ws"
  ```
- `warman/translation/ja.ws:18:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ja/multiplayer.ws"
  ```
- `warman/translation/ja.ws:19:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ja/workshop.ws"
  ```
- `warman/translation/ja.ws:20:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ja/codex.ws"
  ```
- `warman/translation/ja.ws:21:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ja/general.ws"
  ```

## warman/translation/ko.ws

- `warman/translation/ko.ws:7:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ko/menus.ws"
  ```
- `warman/translation/ko.ws:8:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ko/options.ws"
  ```
- `warman/translation/ko.ws:9:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ko/alerts.ws"
  ```
- `warman/translation/ko.ws:10:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ko/tooltips.ws"
  ```
- `warman/translation/ko.ws:11:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ko/skills.ws"
  ```
- `warman/translation/ko.ws:12:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ko/mastery.ws"
  ```
- `warman/translation/ko.ws:13:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ko/effects.ws"
  ```
- `warman/translation/ko.ws:14:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ko/items.ws"
  ```
- `warman/translation/ko.ws:15:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ko/characters.ws"
  ```
- `warman/translation/ko.ws:16:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ko/world.ws"
  ```
- `warman/translation/ko.ws:17:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ko/story.ws"
  ```
- `warman/translation/ko.ws:18:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ko/multiplayer.ws"
  ```
- `warman/translation/ko.ws:19:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ko/workshop.ws"
  ```
- `warman/translation/ko.ws:20:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ko/codex.ws"
  ```
- `warman/translation/ko.ws:21:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ko/general.ws"
  ```

## warman/translation/pt.ws

- `warman/translation/pt.ws:7:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/pt/menus.ws"
  ```
- `warman/translation/pt.ws:8:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/pt/options.ws"
  ```
- `warman/translation/pt.ws:9:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/pt/alerts.ws"
  ```
- `warman/translation/pt.ws:10:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/pt/tooltips.ws"
  ```
- `warman/translation/pt.ws:11:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/pt/skills.ws"
  ```
- `warman/translation/pt.ws:12:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/pt/mastery.ws"
  ```
- `warman/translation/pt.ws:13:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/pt/effects.ws"
  ```
- `warman/translation/pt.ws:14:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/pt/items.ws"
  ```
- `warman/translation/pt.ws:15:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/pt/characters.ws"
  ```
- `warman/translation/pt.ws:16:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/pt/world.ws"
  ```
- `warman/translation/pt.ws:17:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/pt/story.ws"
  ```
- `warman/translation/pt.ws:18:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/pt/multiplayer.ws"
  ```
- `warman/translation/pt.ws:19:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/pt/workshop.ws"
  ```
- `warman/translation/pt.ws:20:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/pt/codex.ws"
  ```
- `warman/translation/pt.ws:21:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/pt/general.ws"
  ```

## warman/translation/ru.ws

- `warman/translation/ru.ws:7:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ru/menus.ws"
  ```
- `warman/translation/ru.ws:8:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ru/options.ws"
  ```
- `warman/translation/ru.ws:9:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ru/alerts.ws"
  ```
- `warman/translation/ru.ws:10:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ru/tooltips.ws"
  ```
- `warman/translation/ru.ws:11:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ru/skills.ws"
  ```
- `warman/translation/ru.ws:12:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ru/mastery.ws"
  ```
- `warman/translation/ru.ws:13:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ru/effects.ws"
  ```
- `warman/translation/ru.ws:14:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ru/items.ws"
  ```
- `warman/translation/ru.ws:15:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ru/characters.ws"
  ```
- `warman/translation/ru.ws:16:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ru/world.ws"
  ```
- `warman/translation/ru.ws:17:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ru/story.ws"
  ```
- `warman/translation/ru.ws:18:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ru/multiplayer.ws"
  ```
- `warman/translation/ru.ws:19:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ru/workshop.ws"
  ```
- `warman/translation/ru.ws:20:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ru/codex.ws"
  ```
- `warman/translation/ru.ws:21:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/ru/general.ws"
  ```

## warman/translation/zh.ws

- `warman/translation/zh.ws:7:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/zh/menus.ws"
  ```
- `warman/translation/zh.ws:8:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/zh/options.ws"
  ```
- `warman/translation/zh.ws:9:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/zh/alerts.ws"
  ```
- `warman/translation/zh.ws:10:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/zh/tooltips.ws"
  ```
- `warman/translation/zh.ws:11:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/zh/skills.ws"
  ```
- `warman/translation/zh.ws:12:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/zh/mastery.ws"
  ```
- `warman/translation/zh.ws:13:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/zh/effects.ws"
  ```
- `warman/translation/zh.ws:14:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/zh/items.ws"
  ```
- `warman/translation/zh.ws:15:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/zh/characters.ws"
  ```
- `warman/translation/zh.ws:16:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/zh/world.ws"
  ```
- `warman/translation/zh.ws:17:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/zh/story.ws"
  ```
- `warman/translation/zh.ws:18:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/zh/multiplayer.ws"
  ```
- `warman/translation/zh.ws:19:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/zh/workshop.ws"
  ```
- `warman/translation/zh.ws:20:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/zh/codex.ws"
  ```
- `warman/translation/zh.ws:21:1` **import-order** (manual): This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand.
  ```
  import "translation/zh/general.ws"
  ```
