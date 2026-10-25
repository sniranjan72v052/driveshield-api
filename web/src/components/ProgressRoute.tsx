import type { CaseComment, CaseStatus, TimelineEntry } from "../api/types";
import { STEPS, formatTime } from "../api/labels";
import CommentItem from "./CommentItem";
import StepComposer from "./StepComposer";

interface Props {
  caseId: string;
  status: CaseStatus;
  timeline: TimelineEntry[];
  comments: CaseComment[];
  /** True for the customer, a provider who accepted the case, and admins. */
  canPost: boolean;
  /** The signed-in person is an admin: they can remove anyone's post or photo. */
  isAdmin: boolean;
  /** Called after a post is added, edited or deleted, so the page can reload the case. */
  onPosted: () => void | Promise<void>;
}

/**
 * The case progress drawn as a route: finished stops are filled, the current stop is marked in
 * hazard yellow, and the stops still ahead are outlined. Under each stop: the system notes, then the
 * photos and comments people added to that step, then (for people who may) a way to add one.
 */
export default function ProgressRoute({ caseId, status, timeline, comments, canPost, isAdmin, onPosted }: Props) {
  const reached = timeline.map((t) => STEPS.findIndex((s) => s.status === t.status)).filter((i) => i >= 0);
  const currentIdx = reached.length ? Math.max(...reached) : 0;
  const finished = status === "Reviewed";
  const stopped = status === "Cancelled";

  return (
    <ol className="route" aria-label="Case progress">
      {STEPS.map((step, i) => {
        const entries = timeline.filter((t) => t.status === step.status);
        const stepComments = comments.filter((c) => c.step === step.status);
        const state = i < currentIdx || (finished && i === currentIdx) ? "done" : i === currentIdx ? "current" : "ahead";
        const className = `route-stop ${state}${stopped && state !== "done" ? " halted" : ""}`;
        return (
          <li key={step.status} className={className} aria-current={state === "current" ? "step" : undefined}>
            <span className="route-marker" aria-hidden="true" />
            <div className="route-body">
              <strong>{step.label}</strong>
              {entries.map((e) => (
                <p key={e.id} className="route-note">
                  {e.note ? `${e.note} ` : ""}
                  <time dateTime={e.at}>{formatTime(e.at)}</time>
                </p>
              ))}

              {stepComments.length > 0 && (
                <ul className="thread">
                  {stepComments.map((c) => (
                    <CommentItem key={c.id} caseId={caseId} comment={c} isAdmin={isAdmin} onChanged={onPosted} />
                  ))}
                </ul>
              )}

              {canPost && state !== "ahead" && (
                <StepComposer caseId={caseId} step={step.status} stepLabel={step.label} onPosted={onPosted} />
              )}
            </div>
          </li>
        );
      })}
    </ol>
  );
}
