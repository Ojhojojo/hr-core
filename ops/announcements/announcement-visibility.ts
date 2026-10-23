import type { StatusBadgeTone } from '@hr-host';

export interface Announcement {
  id: string;
  title: string;
  body: string;
  startDate: string;
  endDate: string;
  active: boolean;
}

export type AnnouncementState = 'active' | 'scheduled' | 'expired' | 'inactive';

export interface AnnouncementStatus {
  status: AnnouncementState;
  label: 'Active' | 'Scheduled' | 'Expired' | 'Inactive';
  tone: StatusBadgeTone;
}

export type AnnouncementStatusMark = AnnouncementStatus;

/** Active rows whose start and end dates include today. An end date of yesterday is hidden. */
export function isAnnouncementVisible(
  row: Pick<Announcement, 'active' | 'startDate' | 'endDate'>,
  today: string,
): boolean {
  return row.active && row.startDate <= today && row.endDate >= today;
}

/**
 * Returns badge label, status state, and tone for an announcement.
 * - Active: turned on and today is within startDate..endDate
 * - Scheduled: turned on and starts in the future
 * - Expired: turned on and endDate has passed
 * - Inactive: turned off
 */
export function announcementStatus(
  row: Pick<Announcement, 'active' | 'startDate' | 'endDate'>,
  today: string,
): AnnouncementStatus {
  if (!row.active) {
    return { status: 'inactive', label: 'Inactive', tone: 'neutral' };
  }
  if (row.startDate > today) {
    return { status: 'scheduled', label: 'Scheduled', tone: 'info' };
  }
  if (row.endDate < today) {
    return { status: 'expired', label: 'Expired', tone: 'neutral' };
  }
  return { status: 'active', label: 'Active', tone: 'ok' };
}
