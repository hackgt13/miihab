using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Kinesthetic.Visit
{
    /// What happens in a visit: the whiteboard's program updates and the lines the therapist says, from the
    /// coordinator's GET /api/visit (coordinator/visit.ts). Parsed with JObject rather than reflection, which
    /// IL2CPP strips.
    ///
    /// Each line says how many board updates are showing once it starts (`Reveal`), so the therapist talks
    /// about each update as it is written up, and `Audio` is where a recorded voice goes. It is null today:
    /// the visit shows the line as a caption, paced as if spoken (CaptionVoice).
    public sealed class VisitScript
    {
        public sealed class Update { public string Kind, Heading, Detail; }
        public sealed class Line { public string Id, Text, Audio; public int Reveal; }
        public sealed class QuickReply { public string Kind, Label; }

        public string TherapistName = "Alex", Credentials = "PT, DPT";
        public bool SampleTherapist = true;
        public string Title = "Program updates", Attribution = "From your licensed therapist", UpdatedAt = "";
        public Update[] Updates = new Update[0];
        public Line[] Lines = new Line[0];
        /// False when this is the stand-in shown because the coordinator could not be reached.
        public bool Live;
        /// The plan version this visit talks about, which the visit marks seen once it has been through it.
        public int PlanVersion;
        /// The question the visit ends on, or null when there is nobody to relay an answer to (offline).
        public string AskPrompt;
        public QuickReply[] QuickReplies = new QuickReply[0];

        public static VisitScript FromCoordinator(JObject json)
        {
            var therapist = json["therapist"];
            var board = json["board"];
            return new VisitScript
            {
                TherapistName = (string)therapist?["name"] ?? "Alex",
                Credentials = (string)therapist?["credentials"] ?? "",
                SampleTherapist = (bool?)therapist?["sample"] ?? true,
                Title = (string)board?["title"] ?? "Program updates",
                Attribution = (string)board?["attribution"] ?? "From your licensed therapist",
                UpdatedAt = (string)board?["updatedAt"] ?? "",
                Updates = board?["updates"]?.Select(u => new Update {
                    Kind = (string)u["kind"] ?? "note", Heading = (string)u["heading"] ?? "", Detail = (string)u["detail"] ?? "",
                }).ToArray() ?? new Update[0],
                Lines = json["speech"]?.Select(l => new Line {
                    Id = (string)l["id"], Text = (string)l["text"] ?? "", Audio = (string)l["audio"], Reveal = (int?)l["reveal"] ?? 0,
                }).ToArray() ?? new Line[0],
                PlanVersion = (int?)json["planVersion"] ?? 0,
                AskPrompt = (string)json["ask"]?["prompt"],
                QuickReplies = json["ask"]?["quickReplies"]?.Select(r => new QuickReply { Kind = (string)r["kind"], Label = (string)r["label"] })
                    .ToArray() ?? new QuickReply[0],
                Live = true,
            };
        }

        /// The coordinator is off. The therapist still greets the patient, and says plainly that there is
        /// nothing to show rather than inventing a program.
        public static VisitScript Offline() => new()
        {
            Lines = new[]
            {
                new Line { Id = "offline-0", Text = "Hi, I'm Alex, your physical therapist. Good to see you." },
                new Line { Id = "offline-1", Text = "I can't pull up your program from here right now, so the board is empty. Your updates will be here once we're connected." },
            },
        };

        /// "Updated Sep 26", or empty when the date will not parse.
        public string UpdatedCaption() =>
            DateTime.TryParse(UpdatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var at)
                ? "Updated " + at.ToLocalTime().ToString("MMM d") : "";
    }
}
