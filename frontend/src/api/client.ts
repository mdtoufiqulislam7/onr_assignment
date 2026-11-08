// API Client supporting environment variable backend URL (both runtime Docker env and Vite build-time env)

export interface ClassItem {
  id: string;
  name: string;
  totalSeats: number;
  heldSeats: number;
  confirmedSeats: number;
  availableSeats: number;
}

export interface HoldItem {
  holdId: string;
  classId: string;
  className?: string;
  studentId?: string;
  status: 'HELD' | 'CONFIRMED' | 'EXPIRED' | 'CANCELLED';
  expiresAt: string;
}

declare global {
  interface Window {
    __ENV__?: {
      VITE_API_URL?: string;
    };
  }
}

const getBaseUrl = (): string => {
  if (typeof window !== 'undefined' && window.__ENV__?.VITE_API_URL) {
    return window.__ENV__.VITE_API_URL.replace(/\/+$/, '');
  }
  return (import.meta.env.VITE_API_URL || 'http://localhost:8080').replace(/\/+$/, '');
};

export async function apiRequest<T>(endpoint: string, options: RequestInit = {}, studentId?: string): Promise<T> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(options.headers as Record<string, string> || {}),
  };

  if (studentId) {
    headers['X-Student-Id'] = studentId;
  }

  let response: Response;
  try {
    response = await fetch(`${getBaseUrl()}${endpoint}`, {
      ...options,
      headers,
    });
  } catch (err) {
    throw new Error('সার্ভারের সাথে সংযোগ স্থাপন করা সম্ভব হচ্ছে না। অনুগ্রহ করে ব্যাকএন্ড চালু আছে কিনা যাচাই করুন।');
  }

  if (response.status === 204) {
    return {} as T;
  }

  const data = await response.json().catch(() => null);

  if (!response.ok) {
    if (response.status === 409) {
      throw new Error(data?.message || 'দুঃখিত! এই ক্লাসে কোনো আসন খালি নেই অথবা আপনার একটি আবেদন ইতিমধ্যে সক্রিয় রয়েছে।');
    }
    if (response.status === 403) {
      throw new Error(data?.message || 'এই আবেদনটি পরিচালনা করার অনুমতি আপনার নেই।');
    }
    if (response.status === 429) {
      throw new Error('একসাথে অতিরিক্ত অনুরোধ পাঠানো হয়েছে। অনুগ্রহ করে কয়েক সেকেন্ড অপেক্ষা করে আবার চেষ্টা করুন।');
    }
    throw new Error(data?.message || `অনুরোধ ব্যর্থ হয়েছে (${response.status})`);
  }

  return data as T;
}
