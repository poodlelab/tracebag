import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class TracebagSseService {
  open<T>(
    url: string,
    eventName: string,
    onMessage: (payload: T) => void,
    onError: () => void
  ): EventSource {
    const source = new EventSource(url);
    source.addEventListener(eventName, (event) => {
      onMessage(JSON.parse((event as MessageEvent<string>).data) as T);
    });
    source.onerror = onError;
    return source;
  }
}
