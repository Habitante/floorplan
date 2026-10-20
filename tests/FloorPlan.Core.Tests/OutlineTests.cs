using System.Text;

namespace FloorPlan.Core.Tests;

public class OutlineTests
{
    static FileOutline Outline(string path, string text) => Outlines.Of(path, Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n")))!;

    /// <summary>Each symbol as "kind id from-to", sections by their title.</summary>
    static string[] Symbols(FileOutline outline) =>
        outline.All().Select(s => $"{s.Kind} {s.Id ?? "[" + s.Name + "]"} {s.From}-{s.To}").ToArray();

    static string[] Folds(FileOutline outline) => outline.Folds.Select(f => $"{f.From}-{f.To}").ToArray();

    const string Player = """
        class_name Player
        extends CharacterBody3D
        ## The player: walks and jumps.

        signal landed(height: float)

        const SPEED := 5.0
        @export var jump := 4.5
        var _air := false

        enum State { IDLE, WALK, JUMP }

        # --- moving ---

        ## Moves the player each frame,
        ## falling while in the air.
        func _physics_process(delta: float) -> void:
            velocity.y -= 9.8 * delta
            if is_on_floor():
                _air = false
            move_and_slide()

        ## Jumps at once.
        func jump_now() -> void:
            velocity.y = jump

        class Step:
            var height := 0.0
            func _init(h: float) -> void:
                height = h
        """;

    [Fact]
    public void A_script_outlines_its_data_functions_inner_classes_and_banner_sections()
    {
        var outline = Outline("scenes/player.gd", Player);

        Assert.Equal("gdscript", outline.Language);
        Assert.Equal(
            [
                "signal landed 5-5", "constant SPEED 7-7", "variable jump 8-8", "variable _air 9-9", "enum State 11-11",
                "section [moving] 13-30",
                "function _physics_process 15-21",     // with the comments above it
                "function jump_now 23-25",
                "class Step 27-30", "variable Step.height 28-28", "function Step._init 29-30",
            ],
            Symbols(outline));
        // Only bodies of four lines or more fold; short ones read in place.
        Assert.Equal(["18-21"], Folds(outline));
        Assert.Null(outline.Errors);
    }

    [Fact]
    public void Finds_a_symbol_by_its_id_or_its_own_name_and_the_one_around_some_lines()
    {
        var outline = Outline("scenes/player.gd", Player);

        Assert.Equal("Step._init", Outlines.Find(outline, "Step._init")!.Id);
        Assert.Equal("Step._init", Outlines.Find(outline, "step._INIT")!.Id);
        Assert.Equal("Step._init", Outlines.Find(outline, "_init")!.Id);     // its own name, the only one
        Assert.Null(Outlines.Find(outline, "nothing"));
        Assert.Equal("_physics_process", Outlines.Around(outline, 19, 20)!.Id);
        Assert.Equal("moving", Outlines.Around(outline, 26, 26)!.Name);         // between functions: their section
        Assert.Null(Outlines.Around(outline, 4, 4));
    }

    [Fact]
    public void Python_folds_docstrings_to_their_first_line_and_keeps_decorators_with_their_function()
    {
        var outline = Outline("tools/export.py", """"
            """Exports the level.

            Run inside Blender with the level open.
            It writes one mesh per room.
            """

            import math

            SCALE = 2.0


            @cache
            def scale(x):
                """The size of x in the game."""
                a = x * 2
                b = a + 1
                c = b * SCALE
                return c


            class Room:
                """A room and its doors."""
                doors = 2

                def exits(self, n):
                    return [n] * self.doors


            if __name__ == "__main__":
                print(scale(1))
            """");

        Assert.Equal(
            ["variable SCALE 9-9", "function scale 12-18", "class Room 21-26", "variable Room.doors 23-23", "function Room.exits 25-26", "main (main) 29-30"],
            Symbols(outline));
        Assert.Equal(["2-5", "15-18"], Folds(outline));
    }

    [Fact]
    public void JavaScript_names_methods_by_the_object_keys_that_hold_them_and_tests_by_their_strings()
    {
        var outline = Outline("web/players.js", """
            // The players and how they move.
            const PLAYERS = {
              ann: {
                move(world) {
                  return world.left
                },
              },
              bob: {
                move(world) {
                  const a = 1
                  const b = 2
                  const c = 3
                  return a + b + c
                },
              },
            }

            export function play(world) {
              return PLAYERS.ann.move(world)
            }

            class Match {
              start() {
                this.on = true
              }
            }

            describe('players', () => {
              it('move', () => {
                play({})
              })
            })
            """);

        Assert.Equal(
            [
                "variable PLAYERS 1-16", "object PLAYERS.ann 3-7", "method PLAYERS.ann.move 4-6", "object PLAYERS.bob 8-15", "method PLAYERS.bob.move 9-14",
                "function play 18-20", "class Match 22-26", "method Match.start 23-25", "test players 28-32", "test players.move 29-31",
            ],
            Symbols(outline));
        // Data that holds methods, and tests that hold tests, stay open so what they hold reads.
        Assert.Equal(["10-14"], Folds(outline));
    }

    [Fact]
    public void TypeScript_outlines_types_interfaces_and_exported_arrow_functions()
    {
        var outline = Outline("web/src/layout.ts", """
            export interface Scene {
              tree: Node
            }

            export type Lens = 'kind' | 'age'

            export enum Mode { Walk, Sit }

            export const fillFor = (scene: Scene): string => {
              const a = 1
              const b = 2
              const c = 3
              return `${a}${b}${c}`
            }

            export class Layout {
              width = 0
              place(x: number): number {
                return x * this.width
              }
            }
            """);

        Assert.Equal(
            ["interface Scene 1-3", "type Lens 5-5", "enum Mode 7-7", "function fillFor 9-14", "class Layout 16-21", "field Layout.width 17-17", "method Layout.place 18-20"],
            Symbols(outline));
        Assert.Equal(["10-14"], Folds(outline));
        Assert.Equal("tsx", Outline("web/src/finder.tsx", "export function Finder() { return <div /> }\n").Language);
    }

    [Fact]
    public void CSharp_regions_become_sections_namespaces_name_nothing_and_overloads_are_numbered()
    {
        var outline = Outline("src/Shape.cs", """
            namespace Demo
            {
                /// <summary>A shape.</summary>
                public class Shape
                {
                    int sides;
                    public string Name { get; set; } = "";

                    public Shape(int sides) => this.sides = sides;

                    #region Measuring
                    public double Area(double r)
                    {
                        var a = r * r;
                        return a * Math.PI;
                    }

                    public double Area(double w, double h) => w * h;
                    #endregion
                }

                public record Point(int X, int Y);
            }
            """);

        Assert.Equal(
            [
                "namespace [Demo] 1-23", "class Shape 3-20", "field Shape.sides 6-6", "property Shape.Name 7-7", "constructor Shape.Shape 9-9",
                "section [Measuring] 11-19", "method Shape.Area 12-16", "method Shape.Area~2 18-18", "record Point 22-22",
            ],
            Symbols(outline));
        Assert.Equal(["13-16"], Folds(outline));
    }

    [Fact]
    public void A_shader_outlines_its_uniform_groups_and_functions_past_what_its_grammar_misreads()
    {
        var outline = Outline("shaders/glow.gdshader", """
            shader_type spatial;
            render_mode unshaded;

            group_uniforms Glow;
            uniform float glow : hint_range(0.0, 2.0) = 0.6;
            uniform vec3 tint : source_color = vec3(1.0);
            group_uniforms;

            varying vec3 local;
            const float SCALE = 2.0;

            struct Ray {
                vec3 origin;
                vec3 dir;
            };

            void vertex() {
                local = VERTEX;
            }

            void fragment() {
                float a = 1e-6;
                ALBEDO = tint * glow;
                ALPHA = 1.0;
                EMISSION = vec3(a);
            }
            """);

        Assert.Equal(
            [
                "section [Glow] 4-7", "uniform glow 5-5", "uniform tint 6-6", "varying local 9-9", "constant SCALE 10-10",
                "struct Ray 12-15", "function vertex 17-19", "function fragment 21-26",
            ],
            Symbols(outline));
        Assert.Equal(["22-26"], Folds(outline));
        Assert.True(outline.Errors > 0);    // the grammar does not read 1e-6
    }

    [Fact]
    public void A_document_outlines_its_headings_outside_code_with_anchors_as_GitHub_makes_them()
    {
        var outline = Outline("DESIGN.md", """
            # FloorPlan: design

            Intro line.

            ## Milestones

            Text one.
            Text two.
            Text three.

            ```text
            # not a heading
            ```

            ### 5. Inside files

            More.

            ## Milestones

            Setext `Heading`
            ----------------
            Body.
            """);

        Assert.Equal("markdown", outline.Language);
        Assert.Equal(
            ["section floorplan-design 1-23", "section milestones 5-18", "section 5-inside-files 15-18", "section milestones-1 19-20", "section setext-heading 21-23"],
            Symbols(outline));
        Assert.Equal("Setext `Heading`", outline.All().Last().Name);
        // A section's own text folds when it is long enough; its subsections keep their headings in view.
        Assert.Equal(["6-14"], Folds(outline));
    }

    [Fact]
    public void A_symbols_id_survives_edits_elsewhere_in_its_file()
    {
        var before = Outline("scenes/player.gd", Player);
        var edited = Player
            .Replace("const SPEED := 5.0", "const SPEED := 6.5\nconst RUN := 9.0")
            .Replace("    move_and_slide()", "    move_and_slide()\n    _air = not is_on_floor()\n    landed.emit(0.0)")
            + "\n\nfunc land() -> void:\n    _air = false\n";
        var after = Outline("scenes/player.gd", edited);

        var ids = after.All().Select(s => s.Id).ToList();
        Assert.All(before.All().Where(s => s.Id is not null), s => Assert.Contains(s.Id, ids));
        Assert.Contains("RUN", ids);
        Assert.Contains("land", ids);
        Assert.Equal(before.All().Single(s => s.Id == "jump_now").To + 3, after.All().Single(s => s.Id == "jump_now").To);
    }

    [Fact]
    public async Task Files_without_an_outline_say_why_when_it_matters()
    {
        Assert.False(Outlines.Supports("notes.txt"));
        Assert.False(Outlines.Supports("art/desk.png"));
        Assert.True(Outlines.Supports("README.md"));
        Assert.Null(Outlines.Of("notes.txt", Encoding.UTF8.GetBytes("# a\n")));
        Assert.Null(Outlines.Of("x.gd", [0x66, 0x00, 0x75, 0x6e]));     // binary
        Assert.Equal(["function f 1-2"], Symbols(Outlines.Of("x.gd", [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("func f():\n    pass\n")])!));
        Assert.Empty(Outline("empty.py", "").Symbols);

        using var repo = new TempRepo(git: false).Write("big.gd", new string('#', Outlines.MaxBytes + 10));
        var (outline, note) = await Outlines.ReadAsync(Path.Combine(repo.Root, "big.gd"), "big.gd", CancellationToken.None);
        Assert.Null(outline);
        Assert.StartsWith("Too large to outline", note);
    }
}
