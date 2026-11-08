import React, { useState, useEffect } from 'react';
import { apiRequest, ClassItem, HoldItem } from './api/client';
import { School, User, PlusCircle, CheckCircle, Clock, XCircle, RefreshCw, AlertCircle } from 'lucide-react';

export default function App() {
  const [studentId, setStudentId] = useState<string>(() => {
    return localStorage.getItem('school_student_id') || '20152691234567890';
  });
  const [activeTab, setActiveTab] = useState<'classes' | 'my_holds' | 'admin'>('classes');
  
  const [classes, setClasses] = useState<ClassItem[]>([]);
  const [myHolds, setMyHolds] = useState<HoldItem[]>([]);
  const [loading, setLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  // Pagination states
  const [classPage, setClassPage] = useState<number>(1);
  const pageSize = 10;

  // Admin form state
  const [newClassName, setNewClassName] = useState<string>('');
  const [newTotalSeats, setNewTotalSeats] = useState<number>(120);

  // Save studentId to localStorage
  const handleStudentIdChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const val = e.target.value.trim();
    setStudentId(val);
    localStorage.setItem('school_student_id', val);
  };

  const showNotification = (msg: string, isError = false) => {
    if (isError) {
      setError(msg);
      setSuccessMsg(null);
    } else {
      setSuccessMsg(msg);
      setError(null);
    }
    setTimeout(() => {
      setError(null);
      setSuccessMsg(null);
    }, 6000);
  };

  // Fetch classes
  const fetchClasses = async () => {
    try {
      const data = await apiRequest<ClassItem[]>(`/classes?page=${classPage}&pageSize=${pageSize}`);
      setClasses(data);
    } catch (err: any) {
      // Don't flash big error on silent polling
    }
  };

  // Fetch my holds
  const fetchMyHolds = async () => {
    if (!studentId) return;
    try {
      const data = await apiRequest<HoldItem[]>('/me/holds', {}, studentId);
      setMyHolds(data);
    } catch (err: any) {
      // Silent catch for polling
    }
  };

  // Auto-poll seat counts every 3 seconds without page reload (Section 1.5 Rule 10)
  useEffect(() => {
    fetchClasses();
    fetchMyHolds();

    const interval = setInterval(() => {
      fetchClasses();
      fetchMyHolds();
    }, 3000);

    return () => clearInterval(interval);
  }, [studentId, classPage]);

  // Client-side 1s ticker for live countdown & instant auto-expire status transition (Rule 9)
  const [, setTick] = useState(0);
  useEffect(() => {
    const ticker = setInterval(() => {
      setTick(t => t + 1);
    }, 1000);
    return () => clearInterval(ticker);
  }, []);

  // Action: Hold Seat (Apply)
  const handleHoldSeat = async (classItem: ClassItem) => {
    if (!studentId || studentId.length < 5) {
      showNotification('অনুগ্রহ করে পেজের উপরে সঠিক শিক্ষার্থীর জন্ম নিবন্ধন নম্বর দিন।', true);
      return;
    }

    setLoading(true);
    try {
      const res = await apiRequest<HoldItem>(`/classes/${classItem.id}/holds`, { method: 'POST' }, studentId);
      showNotification(`আসন সফলভাবে Hold হয়েছে! ১০ মিনিটের মধ্যে ফি পরিশোধ করুন।`);
      await fetchClasses();
      await fetchMyHolds();
      setActiveTab('my_holds');
    } catch (err: any) {
      showNotification(err.message, true);
    } finally {
      setLoading(false);
    }
  };

  // Action: Confirm Hold (Pay Fee)
  const handleConfirmHold = async (holdId: string) => {
    setLoading(true);
    try {
      await apiRequest(`/holds/${holdId}/confirm`, { method: 'POST' }, studentId);
      showNotification('ভর্তি ফি সফলভাবে জমা হয়েছে! আপনার ভর্তি নিশ্চিত হয়েছে।');
      await fetchClasses();
      await fetchMyHolds();
    } catch (err: any) {
      showNotification(err.message, true);
    } finally {
      setLoading(false);
    }
  };

  // Action: Cancel Hold
  const handleCancelHold = async (holdId: string) => {
    if (!window.confirm('আপনি কি নিশ্চিত যে আবেদনটি বাতিল করতে চান?')) return;
    setLoading(true);
    try {
      await apiRequest(`/holds/${holdId}`, { method: 'DELETE' }, studentId);
      showNotification('আবেদনটি বাতিল করা হয়েছে এবং আসন উন্মুক্ত করা হয়েছে।');
      await fetchClasses();
      await fetchMyHolds();
    } catch (err: any) {
      showNotification(err.message, true);
    } finally {
      setLoading(false);
    }
  };

  // Action: Create Class (School Admin)
  const handleCreateClass = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newClassName.trim()) {
      showNotification('ক্লাস এবং শিফটের নাম অবশ্যই দিতে হবে।', true);
      return;
    }
    if (newTotalSeats <= 0) {
      showNotification('আসন সংখ্যা ০ এর বেশি হতে হবে।', true);
      return;
    }

    setLoading(true);
    try {
      await apiRequest('/classes', {
        method: 'POST',
        body: JSON.stringify({ name: newClassName.trim(), totalSeats: newTotalSeats }),
      });
      showNotification(`নতুন ক্লাস "${newClassName}" সফলভাবে তৈরি হয়েছে!`);
      setNewClassName('');
      setNewTotalSeats(120);
      await fetchClasses();
      setActiveTab('classes');
    } catch (err: any) {
      showNotification(err.message, true);
    } finally {
      setLoading(false);
    }
  };

  // Format countdown remaining seconds
  const formatTimeRemaining = (expiresAtStr: string) => {
    const diff = Math.floor((new Date(expiresAtStr).getTime() - Date.now()) / 1000);
    if (diff <= 0) return { isExpired: true, text: 'মেয়াদ শেষ (Expired)' };
    const mins = Math.floor(diff / 60);
    const secs = diff % 60;
    return {
      isExpired: false,
      text: `${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')} মিনিট বাকি`,
    };
  };

  return (
    <div className="max-w-6xl mx-auto px-4 py-8">
      {/* Top Header */}
      <header className="bg-white rounded-2xl shadow-sm border border-slate-200 p-6 mb-6">
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <div className="p-3 bg-blue-600 rounded-xl text-white shadow-md shadow-blue-200">
              <School className="w-8 h-8" />
            </div>
            <div>
              <h1 className="text-2xl font-bold text-slate-800">অনলাইন স্কুল ভর্তি সিস্টেম</h1>
              <p className="text-sm text-slate-500">আগে আসলে আগে পাবেন ভিত্তিতে ভর্তি কার্যক্রম</p>
            </div>
          </div>

          {/* Student ID Input Bar */}
          <div className="bg-slate-50 border border-slate-200 rounded-xl p-3 flex items-center gap-3">
            <User className="w-5 h-5 text-blue-600" />
            <div>
              <label className="block text-xs font-semibold text-slate-600">শিক্ষার্থীর জন্ম নিবন্ধন নম্বর (X-Student-Id)</label>
              <input
                type="text"
                value={studentId}
                onChange={handleStudentIdChange}
                placeholder="১৭ অঙ্কের জন্ম নিবন্ধন নম্বর"
                className="bg-white border border-slate-300 rounded px-3 py-1 text-sm font-mono w-64 focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
          </div>
        </div>

        {/* Global Notifications */}
        {error && (
          <div className="mt-4 p-4 bg-red-50 border border-red-200 rounded-xl flex items-center gap-3 text-red-700 text-sm">
            <AlertCircle className="w-5 h-5 flex-shrink-0" />
            <span>{error}</span>
          </div>
        )}
        {successMsg && (
          <div className="mt-4 p-4 bg-emerald-50 border border-emerald-200 rounded-xl flex items-center gap-3 text-emerald-700 text-sm">
            <CheckCircle className="w-5 h-5 flex-shrink-0" />
            <span>{successMsg}</span>
          </div>
        )}
      </header>

      {/* Navigation Tabs */}
      <div className="flex border-b border-slate-200 mb-6 gap-2">
        <button
          onClick={() => setActiveTab('classes')}
          className={`px-5 py-3 font-medium text-sm flex items-center gap-2 border-b-2 transition-colors ${
            activeTab === 'classes' ? 'border-blue-600 text-blue-600 font-semibold' : 'border-transparent text-slate-500 hover:text-slate-800'
          }`}
        >
          <School className="w-4 h-4" />
          ক্লাস ও আসন তালিকা
        </button>

        <button
          onClick={() => setActiveTab('my_holds')}
          className={`px-5 py-3 font-medium text-sm flex items-center gap-2 border-b-2 transition-colors ${
            activeTab === 'my_holds' ? 'border-blue-600 text-blue-600 font-semibold' : 'border-transparent text-slate-500 hover:text-slate-800'
          }`}
        >
          <Clock className="w-4 h-4" />
          আমার আবেদন {myHolds.length > 0 && <span className="bg-blue-100 text-blue-700 text-xs px-2 py-0.5 rounded-full">{myHolds.length}</span>}
        </button>

        <button
          onClick={() => setActiveTab('admin')}
          className={`px-5 py-3 font-medium text-sm flex items-center gap-2 border-b-2 transition-colors ${
            activeTab === 'admin' ? 'border-blue-600 text-blue-600 font-semibold' : 'border-transparent text-slate-500 hover:text-slate-800'
          }`}
        >
          <PlusCircle className="w-4 h-4" />
          ক্লাস তৈরি (School Admin)
        </button>
      </div>

      {/* Tab 1: Class List */}
      {activeTab === 'classes' && (
        <div className="bg-white rounded-2xl shadow-sm border border-slate-200 overflow-hidden">
          <div className="p-5 border-b border-slate-100 flex justify-between items-center bg-slate-50/50">
            <div>
              <h2 className="text-lg font-bold text-slate-800">ভর্তির জন্য উন্মুক্ত ক্লাস সমূহ</h2>
              <p className="text-xs text-slate-500">প্রতি ৩ সেকেন্ড পরপর স্বয়ংক্রিয়ভাবে আসন সংখ্যা হালনাগাদ হচ্ছে</p>
            </div>
            <button
              onClick={() => fetchClasses()}
              className="text-xs text-blue-600 hover:text-blue-800 flex items-center gap-1 font-medium bg-white px-3 py-1.5 rounded-lg border border-slate-200 shadow-sm"
            >
              <RefreshCw className="w-3.5 h-3.5" /> রিফ্রেশ
            </button>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-100 text-slate-600 uppercase text-xs">
                <tr>
                  <th className="py-3 px-5">ক্লাস ও শিফট</th>
                  <th className="py-3 px-4 text-center">মোট আসন</th>
                  <th className="py-3 px-4 text-center">হোল্ড (Hold)</th>
                  <th className="py-3 px-4 text-center">নিশ্চিত</th>
                  <th className="py-3 px-4 text-center">খালি আসন</th>
                  <th className="py-3 px-5 text-right">পদক্ষেপ</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {classes.length === 0 ? (
                  <tr>
                    <td colSpan={6} className="py-10 text-center text-slate-400">
                      কোনো ক্লাস পাওয়া যায়নি। অনুগ্রহ করে স্কুল অ্যাডমিন ট্যাব থেকে নতুন ক্লাস তৈরি করুন।
                    </td>
                  </tr>
                ) : (
                  classes.map(cls => (
                    <tr key={cls.id} className="hover:bg-slate-50/80 transition-colors">
                      <td className="py-4 px-5 font-semibold text-slate-800">{cls.name}</td>
                      <td className="py-4 px-4 text-center font-mono">{cls.totalSeats}</td>
                      <td className="py-4 px-4 text-center">
                        <span className="bg-amber-100 text-amber-800 text-xs font-semibold px-2.5 py-1 rounded-full">
                          {cls.heldSeats}
                        </span>
                      </td>
                      <td className="py-4 px-4 text-center">
                        <span className="bg-emerald-100 text-emerald-800 text-xs font-semibold px-2.5 py-1 rounded-full">
                          {cls.confirmedSeats}
                        </span>
                      </td>
                      <td className="py-4 px-4 text-center">
                        <span className={`text-xs font-bold px-2.5 py-1 rounded-full ${cls.availableSeats > 0 ? 'bg-blue-100 text-blue-800' : 'bg-red-100 text-red-700'}`}>
                          {cls.availableSeats}
                        </span>
                      </td>
                      <td className="py-4 px-5 text-right">
                        <button
                          onClick={() => handleHoldSeat(cls)}
                          disabled={loading || cls.availableSeats <= 0}
                          className={`px-4 py-2 rounded-lg text-xs font-semibold shadow-sm transition-all ${
                            cls.availableSeats > 0
                              ? 'bg-blue-600 hover:bg-blue-700 text-white'
                              : 'bg-slate-200 text-slate-400 cursor-not-allowed'
                          }`}
                        >
                          {cls.availableSeats > 0 ? 'আবেদন করুন' : 'আসন খালি নেই'}
                        </button>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>

          {/* Pagination bar */}
          <div className="p-4 border-t border-slate-100 flex justify-between items-center text-xs text-slate-500">
            <span>পৃষ্ঠা: {classPage}</span>
            <div className="flex gap-2">
              <button
                disabled={classPage <= 1}
                onClick={() => setClassPage(p => Math.max(1, p - 1))}
                className="px-3 py-1 bg-white border border-slate-200 rounded disabled:opacity-40"
              >
                আগের পাতা
              </button>
              <button
                disabled={classes.length < pageSize}
                onClick={() => setClassPage(p => p + 1)}
                className="px-3 py-1 bg-white border border-slate-200 rounded disabled:opacity-40"
              >
                পরের পাতা
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Tab 2: My Holds */}
      {activeTab === 'my_holds' && (
        <div className="bg-white rounded-2xl shadow-sm border border-slate-200 p-6">
          <h2 className="text-lg font-bold text-slate-800 mb-2">এই শিক্ষার্থীর আবেদন ও ভর্তির অবস্থা</h2>
          <p className="text-xs text-slate-500 mb-6">
            আইডি: <span className="font-mono font-semibold text-blue-600">{studentId}</span>
          </p>

          {myHolds.length === 0 ? (
            <div className="py-12 text-center text-slate-400">
              আপনার এখনো কোনো আবেদন নেই। ক্লাস তালিকা থেকে "আবেদন করুন" বাটনে ক্লিক করুন।
            </div>
          ) : (
            <div className="space-y-4">
              {myHolds.map(h => {
                const countdown = formatTimeRemaining(h.expiresAt);
                const isStillHeld = h.status === 'HELD' && !countdown.isExpired;
                const isExpired = h.status === 'EXPIRED' || (h.status === 'HELD' && countdown.isExpired);

                return (
                  <div
                    key={h.holdId}
                    className="p-5 border border-slate-200 rounded-xl bg-slate-50/50 flex flex-col md:flex-row md:items-center justify-between gap-4"
                  >
                    <div>
                      <div className="flex items-center gap-2">
                        <h3 className="font-bold text-slate-800 text-base">{h.className || 'ক্লাস'}</h3>
                        <span className="text-xs font-mono text-slate-400">({h.holdId})</span>
                      </div>

                      <div className="mt-2 flex flex-wrap items-center gap-3 text-xs">
                        {h.status === 'CONFIRMED' && (
                          <span className="bg-emerald-100 text-emerald-800 px-3 py-1 rounded-full font-bold flex items-center gap-1">
                            <CheckCircle className="w-3.5 h-3.5" /> ভর্তি নিশ্চিত (CONFIRMED)
                          </span>
                        )}
                        {isStillHeld && (
                          <span className="bg-amber-100 text-amber-800 px-3 py-1 rounded-full font-bold flex items-center gap-1">
                            <Clock className="w-3.5 h-3.5" /> আসন সংরক্ষিত (HELD) — {countdown.text}
                          </span>
                        )}
                        {isExpired && (
                          <span className="bg-red-100 text-red-700 px-3 py-1 rounded-full font-bold flex items-center gap-1">
                            <XCircle className="w-3.5 h-3.5" /> মেয়াদ উত্তীর্ণ (EXPIRED)
                          </span>
                        )}
                        {h.status === 'CANCELLED' && (
                          <span className="bg-slate-200 text-slate-600 px-3 py-1 rounded-full font-bold flex items-center gap-1">
                            <XCircle className="w-3.5 h-3.5" /> বাতিল করা হয়েছে (CANCELLED)
                          </span>
                        )}
                      </div>
                    </div>

                    {/* Action buttons */}
                    <div className="flex items-center gap-2">
                      <button
                        onClick={() => handleConfirmHold(h.holdId)}
                        disabled={loading || !isStillHeld}
                        className={`px-4 py-2 rounded-lg text-xs font-semibold shadow-sm transition-all ${
                          isStillHeld
                            ? 'bg-emerald-600 hover:bg-emerald-700 text-white'
                            : 'bg-slate-200 text-slate-400 cursor-not-allowed'
                        }`}
                      >
                        ভর্তি ফি দিন (Confirm)
                      </button>

                      {h.status !== 'CANCELLED' && (
                        <button
                          onClick={() => handleCancelHold(h.holdId)}
                          disabled={loading}
                          className="px-4 py-2 rounded-lg text-xs font-semibold bg-white border border-red-300 text-red-600 hover:bg-red-50 transition-all"
                        >
                          বাতিল করুন
                        </button>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      )}

      {/* Tab 3: Admin Create Class */}
      {activeTab === 'admin' && (
        <div className="bg-white rounded-2xl shadow-sm border border-slate-200 p-6 max-w-xl mx-auto">
          <div className="flex items-center gap-2 mb-4">
            <PlusCircle className="w-5 h-5 text-blue-600" />
            <h2 className="text-lg font-bold text-slate-800">নতুন ক্লাস তৈরি (School Admin)</h2>
          </div>
          <p className="text-xs text-slate-500 mb-6">
            প্রতিটি শিফট আলাদা ক্লাস হিসেবে তৈরি হবে এবং নিজস্ব আসন সংখ্যা থাকবে।
          </p>

          <form onSubmit={handleCreateClass} className="space-y-4">
            <div>
              <label className="block text-xs font-semibold text-slate-700 mb-1">
                ক্লাস ও শিফটের নাম (Class Name & Shift)
              </label>
              <input
                type="text"
                value={newClassName}
                onChange={e => setNewClassName(e.target.value)}
                placeholder="যেমন: Class 6 - Morning Shift"
                required
                className="w-full bg-slate-50 border border-slate-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-700 mb-1">
                মোট আসন সংখ্যা (Total Seats)
              </label>
              <input
                type="number"
                min="1"
                value={newTotalSeats}
                onChange={e => setNewTotalSeats(parseInt(e.target.value) || 0)}
                placeholder="যেমন: 120"
                required
                className="w-full bg-slate-50 border border-slate-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>

            <button
              type="submit"
              disabled={loading}
              className="w-full bg-blue-600 hover:bg-blue-700 text-white font-semibold py-2.5 rounded-lg text-sm transition-all shadow-md shadow-blue-100 disabled:opacity-50"
            >
              {loading ? 'তৈরি করা হচ্ছে...' : 'ক্লাস তৈরি করুন'}
            </button>
          </form>
        </div>
      )}
    </div>
  );
}

