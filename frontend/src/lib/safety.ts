// Canonical driver-behavior event codes (DriverBehaviorCatalog) → display labels.
// Shared by the driver Safety Events tab and the trip live-tracking indicator so
// the two surfaces can never drift apart.
export const SAFETY_EVENT_LABELS: Record<string, string> = {
  harsh_braking: 'Harsh Braking',
  harsh_acceleration: 'Harsh Acceleration',
  harsh_cornering: 'Harsh Cornering',
  excessive_idling: 'Excessive Idling',
  drowsiness: 'Drowsiness Detected',
  distraction: 'Driver Distracted',
  phone_usage: 'Phone Usage While Driving',
  sos_triggered: 'Panic Button (SOS)',
};

export function safetyEventLabel(code: string, fallback?: string): string {
  return SAFETY_EVENT_LABELS[code] || fallback || code;
}

export interface SafetyEventLite {
  id: string;
  eventType: string;
  eventTypeName: string;
  confidence: number;
  severity: number;
  eventTimeUtc: string;
  latitude?: number | null;
  longitude?: number | null;
  speedKmh?: number | null;
  mediaUrl?: string | null;
  vehicleName?: string | null;
}