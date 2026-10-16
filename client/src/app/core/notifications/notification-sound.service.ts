import { Injectable, signal } from '@angular/core';

/** Local oscillator, no downloads. Audio is unlocked ONLY by an actual user gesture. */
@Injectable({ providedIn: 'root' })
export class NotificationSound {
  readonly status = signal('Interact with the page or use Test sound to allow audio.');
  private audio?: AudioContext;
  async unlock(): Promise<void> {
    try {
      this.audio ??= new AudioContext();
      if (this.audio.state === 'suspended') await this.audio.resume();
      this.status.set(
        this.audio.state === 'running'
          ? 'Sound is ready on this device.'
          : 'Your browser has paused sound. Try Test sound.',
      );
    } catch {
      this.status.set('Sound is unavailable in this browser. Notifications still work.');
    }
  }
  play(volume: number): void {
    if (!this.audio || this.audio.state !== 'running' || volume <= 0) return;
    try {
      const oscillator = this.audio.createOscillator();
      const gain = this.audio.createGain();
      oscillator.type = 'sine';
      oscillator.frequency.value = 740;
      gain.gain.setValueAtTime(0, this.audio.currentTime);
      gain.gain.linearRampToValueAtTime(
        Math.min(100, Math.max(0, volume)) / 500,
        this.audio.currentTime + 0.015,
      );
      gain.gain.exponentialRampToValueAtTime(0.001, this.audio.currentTime + 0.18);
      oscillator.connect(gain);
      gain.connect(this.audio.destination);
      oscillator.start();
      oscillator.stop(this.audio.currentTime + 0.2);
      oscillator.onended = () => {
        oscillator.disconnect();
        gain.disconnect();
      };
    } catch {
      this.status.set('Sound could not play. Notifications still work.');
    }
  }
  async test(volume: number) {
    await this.unlock();
    this.play(volume);
  }
  reset() {
    const audio = this.audio;
    this.audio = undefined;
    void audio?.close().catch(() => {});
  }
}
