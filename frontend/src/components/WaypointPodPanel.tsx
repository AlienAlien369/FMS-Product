import { useEffect, useState } from 'react';
import api from '../lib/api';
import PodEvidence, { type PodRecord } from './PodEvidence';
import PodCaptureModal from './PodCaptureModal';

/**
 * One owner for a waypoint's proof-of-delivery evidence: fetches its own
 * records, owns the capture modal, and renders the capture button + evidence
 * panel. TripModal passes static facts (permissions, status, refresh tick) and
 * holds no POD state itself — the only cross-component signal is `refreshTick`,
 * bumped whenever the parent refreshes the trip detail, so evidence captured by
 * a driver app elsewhere shows up without a second polling loop.
 */
export default function WaypointPodPanel({ tripId, waypointId, waypointName, waypointType, tripStatus, arrived, canView, canCreate, refreshTick }:
  {
    tripId: string;
    waypointId: string;
    waypointName: string;
    waypointType: number;
    tripStatus: number;
    arrived: boolean;
    canView: boolean;
    canCreate: boolean;
    refreshTick: number;
  }) {

  const [records, setRecords] = useState<PodRecord[]>([]);
  const [modalOpen, setModalOpen] = useState(false);

  useEffect(() => {
    if (!canView) return;
    let alive = true;
    api.get(`/trips/${tripId}/waypoints/${waypointId}/pod`)
      .then(r => { if (alive) setRecords(r.data.data ?? []); })
      .catch(() => {});
    return () => { alive = false; };
  }, [tripId, waypointId, canView, refreshTick]);

  const nothingToShow = !(canCreate && tripStatus === 2 && !arrived)   // capture button
    && !(canView && records.length > 0)                                // evidence
    && !(canView && waypointType === 1 && arrived && records.length === 0); // missing-evidence note
  if (nothingToShow) return null;

  return (
    <div className="mt-2 pl-9 space-y-1.5">
      {canCreate && tripStatus === 2 && !arrived && (
        <button onClick={() => setModalOpen(true)}
          className="px-2 py-1 text-[10px] font-medium bg-blue-100 text-blue-700 rounded hover:bg-blue-200">
          Capture POD
        </button>
      )}
      {canView && records.length > 0 && <PodEvidence records={records} compact />}
      {canView && waypointType === 1 && arrived && records.length === 0 && (
        <p className="text-[10px] text-amber-600">Delivery completed without proof of delivery.</p>
      )}

      {modalOpen && (
        <PodCaptureModal tripId={tripId} waypointId={waypointId} waypointName={waypointName}
          onClose={() => setModalOpen(false)}
          onCaptured={r => { setRecords(rs => [r, ...rs]); setModalOpen(false); }} />
      )}
    </div>
  );
}